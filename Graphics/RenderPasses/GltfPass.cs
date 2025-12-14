using System;
using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.Assets;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.RenderPasses;

public sealed class GltfPass : IRenderPass, IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Pipeline _pipeline;
    private readonly CameraUniformBuffer _cameraBuffer;
    private readonly ModelUniformBuffer _modelBuffer;
    private readonly LightUniformBuffer _lightBuffer;
    private readonly GeometryBuffer _geometryBuffer;

    private readonly List<MeshGpu> _meshes = new();
    private readonly List<MaterialData> _materials = new();
    private readonly List<MeshInstance> _meshInstances = new();
    private readonly Dictionary<SceneNode, int> _nodeToInstance = new();
    private readonly Dictionary<SceneNode, SceneNode?> _nodeParents = new();
    private readonly Dictionary<SceneNode, Matrix4x4> _nodeWorldTransforms = new();
    private bool _loggedFirstInstance;

    private struct MeshInstance
    {
        public readonly MeshGpu Mesh;
        public readonly int MaterialIndex;
        public readonly int TransformIndex; // Maps to SSBO index
        public readonly SceneNode Node;

        public MeshInstance(MeshGpu mesh, int materialIndex, int transformIndex, SceneNode node)
        {
            Mesh = mesh;
            MaterialIndex = materialIndex;
            TransformIndex = transformIndex;
            Node = node;
        }
    }

    public GltfPass(GraphicsDevice gd)
    {
        _gd = gd;
        _geometryBuffer = new GeometryBuffer(gd);

        _cameraBuffer = new CameraUniformBuffer(gd);
        _modelBuffer = new ModelUniformBuffer(gd);
        _lightBuffer = new LightUniformBuffer(gd);

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4),
            new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
        );

        _pipeline = PipelineFactory.CreatePipeline(
            gd,
            "Graphics/Shaders/PBR.vert",
            "Graphics/Shaders/PBR.frag",
            vertexLayout,
            gd.MainSwapchain.Framebuffer,
            enableDepth: true,
            enableBlend: false,
            extraLayouts: new[] { 
                _cameraBuffer.Layout, 
                _modelBuffer.Layout, 
                PbrLayout.MaterialLayout, 
                PbrLayout.MaterialParamsLayout,
                _lightBuffer.Layout,
                _lightBuffer.ParamLayout
            }
        );
    }

    public void AddLight(LightUniform light)
    {
        _lightBuffer.AddLight(light);
    }

    public void LoadScene(SceneAsset scene)
    {
        _gd.WaitForIdle();
        ClearResources();

        LoadMaterials(scene);
        LoadMeshes(scene);
        
        SortAndUploadInstances();
    }

    public void LoadScene(string scenePath)
    {
        var sceneAsset = SceneAsset.Load(scenePath);
        LoadScene(sceneAsset);
    }

    public int InstanceCount => _meshInstances.Count;

    public void UpdateInstanceTransform(int instanceIndex, Matrix4x4 transform)
    {
        if ((uint)instanceIndex >= _meshInstances.Count)
            throw new ArgumentOutOfRangeException(nameof(instanceIndex));

        var instance = _meshInstances[instanceIndex];
        _modelBuffer.UpdateTransform(instance.TransformIndex, transform);
    }

    public bool TryGetWorldTransform(SceneNode node, out Matrix4x4 transform)
    {
        if (_nodeWorldTransforms.TryGetValue(node, out transform))
        {
            if (IsDegenerateMatrix(transform))
            {
                transform = Matrix4x4.Identity;
                _nodeWorldTransforms[node] = transform;
            }
            return true;
        }
        return false;
    }

    public bool ApplyWorldTransform(SceneNode node, Matrix4x4 worldTransform)
    {
        if (!_nodeWorldTransforms.ContainsKey(node))
        {
            return false;
        }

        Matrix4x4 parentWorld = Matrix4x4.Identity;
        if (_nodeParents.TryGetValue(node, out var parent) && parent != null &&
            _nodeWorldTransforms.TryGetValue(parent, out var cachedParentWorld))
        {
            parentWorld = cachedParentWorld;
        }

        if (!Matrix4x4.Invert(parentWorld, out var parentInverse))
        {
            parentInverse = Matrix4x4.Identity;
        }

        node.LocalTransform = worldTransform * parentInverse;
        if (IsDegenerateMatrix(node.LocalTransform))
        {
            node.LocalTransform = Matrix4x4.Identity;
        }
        UpdateWorldRecursive(node, parentWorld);
        return true;
    }

    public void Render(GraphicsDevice gd, CommandList cl, Camera? camera = null)
    {
        if (camera == null || _meshInstances.Count == 0)
            return;

        _cameraBuffer.Update(gd, camera);
        _lightBuffer.UpdateGPU();

        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(1, _modelBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(4, _lightBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(5, _lightBuffer.ParamResourceSet);

        // Bind global geometry buffers once
        cl.SetVertexBuffer(0, _geometryBuffer.VertexBuffer);
        cl.SetIndexBuffer(_geometryBuffer.IndexBuffer, IndexFormat.UInt32);

        int start = 0;
        while (start < _meshInstances.Count)
        {
            var batchInstance = _meshInstances[start];
            var batchMesh = batchInstance.Mesh;
            int batchMaterialIndex = batchInstance.MaterialIndex;

            // Find batch size
            int count = 1;
            while (start + count < _meshInstances.Count)
            {
                var next = _meshInstances[start + count];
                if (next.Mesh != batchMesh || next.MaterialIndex != batchMaterialIndex)
                    break;
                count++;
            }

            // Bind Material
            var material = ResolveMaterial(batchMaterialIndex);
            if (material != null)
            {
                if (material.ResourceSet != null) cl.SetGraphicsResourceSet(2, material.ResourceSet);
                if (material.ParamsResourceSet != null) cl.SetGraphicsResourceSet(3, material.ParamsResourceSet);
            }

            // Draw Instanced
            // indexCount, instanceCount, indexStart, vertexOffset, instanceStart (firstInstance)
            cl.DrawIndexed(
                batchMesh.Range.IndexCount, 
                (uint)count, 
                batchMesh.Range.IndexStart, 
                batchMesh.Range.VertexOffset, 
                (uint)start); // start index corresponds to SSBO index because of SortAndUploadInstances

            start += count;
        }
    }

    private void SortAndUploadInstances()
    {
        if (_meshInstances.Count == 0) return;

        // Sort: Material -> Mesh
        _meshInstances.Sort((a, b) =>
        {
            int cmpMat = a.MaterialIndex.CompareTo(b.MaterialIndex);
            if (cmpMat != 0) return cmpMat;
            return a.Mesh.GetHashCode().CompareTo(b.Mesh.GetHashCode());
        });

        // Rebuild SSBO and update map
        var uniforms = new ModelUniform[_meshInstances.Count];
        _nodeToInstance.Clear();

        for (int i = 0; i < _meshInstances.Count; i++)
        {
            var inst = _meshInstances[i];
            
            // Look up world transform
            if (!_nodeWorldTransforms.TryGetValue(inst.Node, out var world))
                world = Matrix4x4.Identity;

            uniforms[i] = new ModelUniform(world);

            // Update instance with new SSBO index
            _meshInstances[i] = new MeshInstance(inst.Mesh, inst.MaterialIndex, i, inst.Node);
            _nodeToInstance[inst.Node] = i;
        }

        _modelBuffer.EnsureCapacity(_meshInstances.Count);
        _modelBuffer.UpdateAll(uniforms);
    }

    private void LoadMaterials(SceneAsset scene)
    {
        for (int i = 0; i < scene.Materials.Count; i++)
        {
            var src = scene.Materials[i];

            var material = new MaterialData
            {
                Name = src.Name,
                BaseColorFactor = src.BaseColorFactor,
                EmissiveFactor = src.EmissiveFactor,
                MetallicFactor = src.MetallicFactor,
                RoughnessFactor = src.RoughnessFactor,
                UsePackedRMA = !string.IsNullOrEmpty(src.RMAHash)
            };

            material.BaseColorPath = ResolveCachedTexture(src.BaseColorHash);
            material.NormalPath = ResolveCachedTexture(src.NormalHash);
            material.EmissivePath = ResolveCachedTexture(src.EmissiveHash);

            var rmaPath = ResolveCachedTexture(src.RMAHash);
            if (material.UsePackedRMA && rmaPath != null)
            {
                material.MetallicPath = rmaPath;
                material.RoughnessPath = rmaPath;
                material.OcclusionPath = rmaPath;
            }

            MaterialUploader.Upload(_gd, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, material);
            _materials.Add(material);
        }
    }

    private void LoadMeshes(SceneAsset scene)
    {
        if (scene.RootNodes.Count == 0)
            return;

        _loggedFirstInstance = false;
        foreach (var node in scene.RootNodes)
        {
            LoadMeshRecursive(node, Matrix4x4.Identity, null);
        }
    }

    private void LoadMeshRecursive(SceneNode node, Matrix4x4 parentWorld, SceneNode? parent)
    {
        _nodeParents[node] = parent;
        var worldTransform = node.LocalTransform * parentWorld;
        _nodeWorldTransforms[node] = worldTransform;

        if (node.Mesh != null)
        {
            var meshRef = node.Mesh;
            if (TryLoadMesh(meshRef, out MeshGpu? meshGpu))
            {
                var mesh = meshGpu!;
                _meshes.Add(mesh); // Track mesh for disposal

                // Add to instances list. TransformIndex will be assigned in SortAndUploadInstances
                _meshInstances.Add(new MeshInstance(mesh, mesh.MaterialIndex, -1, node));
                
                // _nodeToInstance will be populated in SortAndUploadInstances

                if (!_loggedFirstInstance)
                {
                    _loggedFirstInstance = true;
                    LogInstanceDebug(node, node.LocalTransform, worldTransform);
                }
            }
        }

        foreach (var child in node.Children)
        {
            LoadMeshRecursive(child, worldTransform, node);
        }
    }

    private bool TryLoadMesh(MeshReference meshRef, out MeshGpu? meshGpu)
    {
        meshGpu = null;

        if (!AssetCache.HasMesh(meshRef.MeshHash, out var meshPath))
        {
            Console.WriteLine($"[ScenePass] Missing cached mesh for hash {meshRef.MeshHash}");
            return false;
        }

        try
        {
            var meshData = WlMeshFormat.Read(meshPath, out _);

            if (_materials.Count > 0)
            {
                meshData.MaterialIndex = Math.Clamp(meshRef.MaterialIndex, 0, _materials.Count - 1);
            }
            else
            {
                meshData.MaterialIndex = meshRef.MaterialIndex;
            }
            meshGpu = new MeshGpu(_geometryBuffer, meshData);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScenePass] Failed to load mesh '{meshRef.MeshHash}': {ex.Message}");
            return false;
        }
    }
    
    private MaterialData? ResolveMaterial(int index)
    {
        if (index >= 0 && index < _materials.Count)
        {
            return _materials[index];
        }

        return null;
    }

    private static string? ResolveCachedTexture(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
            return null;

        return AssetCache.HasTexture(hash, out var path) ? path : null;
    }

    private void ClearResources()
    {
        foreach (var mesh in _meshes)
        {
            mesh.Dispose();
        }
        _meshes.Clear();

        foreach (var mat in _materials)
        {
            mat.Dispose();
        }
        _materials.Clear();

        _meshInstances.Clear();
        _modelBuffer.Clear();
        _nodeToInstance.Clear();
        _nodeParents.Clear();
        _nodeWorldTransforms.Clear();
        _loggedFirstInstance = false;
    }

    private void UpdateWorldRecursive(SceneNode node, Matrix4x4 parentWorld)
    {
        var worldTransform = node.LocalTransform * parentWorld;
        if (IsDegenerateMatrix(worldTransform))
        {
            worldTransform = Matrix4x4.Identity;
        }
        _nodeWorldTransforms[node] = worldTransform;

        if (_nodeToInstance.TryGetValue(node, out var instanceIndex))
        {
            // Update SSBO directly
            if (IsDegenerateMatrix(worldTransform))
            {
               worldTransform = Matrix4x4.Identity;
               _nodeWorldTransforms[node] = worldTransform;
            }

            _modelBuffer.UpdateTransform(instanceIndex, worldTransform);
        }

        foreach (var child in node.Children)
        {
            UpdateWorldRecursive(child, worldTransform);
        }
    }

    private static bool IsDegenerateMatrix(in Matrix4x4 matrix)
    {
        const float epsilon = 1e-6f;

        bool allZero = matrix.M11 == 0f && matrix.M12 == 0f && matrix.M13 == 0f && matrix.M14 == 0f &&
                       matrix.M21 == 0f && matrix.M22 == 0f && matrix.M23 == 0f && matrix.M24 == 0f &&
                       matrix.M31 == 0f && matrix.M32 == 0f && matrix.M33 == 0f && matrix.M34 == 0f &&
                       matrix.M41 == 0f && matrix.M42 == 0f && matrix.M43 == 0f && matrix.M44 == 0f;
        if (allZero) return true;

        if (!Matrix4x4.Decompose(matrix, out var scale, out _, out _)) return true;

        if (Math.Abs(scale.X) < epsilon || Math.Abs(scale.Y) < epsilon || Math.Abs(scale.Z) < epsilon) return true;

        return false;
    }

    private static void LogInstanceDebug(SceneNode node, Matrix4x4 local, Matrix4x4 world)
    {
        // Debug logging... (kept minimal)
    }

    public void Dispose()
    {
        ClearResources();
        _cameraBuffer.Dispose();
        _modelBuffer.Dispose();
        _lightBuffer.Dispose();
        _pipeline.Dispose();
        _geometryBuffer.Dispose();
    }
}
