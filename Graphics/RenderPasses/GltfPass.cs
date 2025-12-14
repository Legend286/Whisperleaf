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

    private readonly List<MeshGpu> _meshes = new();
    private readonly List<MaterialData> _materials = new();
    private readonly List<MeshInstance> _meshInstances = new();
    private readonly Dictionary<SceneNode, int> _nodeToInstance = new();
    private readonly Dictionary<SceneNode, SceneNode?> _nodeParents = new();
    private readonly Dictionary<SceneNode, Matrix4x4> _nodeWorldTransforms = new();
    private bool _loggedFirstInstance;

    private readonly struct MeshInstance
    {
        public readonly MeshGpu Mesh;
        public readonly int MaterialIndex;
        public readonly int TransformIndex;
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
        ClearResources();

        LoadMaterials(scene);
        LoadMeshes(scene);
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
        cl.SetGraphicsResourceSet(4, _lightBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(5, _lightBuffer.ParamResourceSet);

        for (int i = 0; i < _meshInstances.Count; i++)
        {
            var instance = _meshInstances[i];
            var mesh = instance.Mesh;

            cl.SetVertexBuffer(0, mesh.VertexBuffer);
            cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
            cl.SetGraphicsResourceSet(1, _modelBuffer.ResourceSet);

            var material = ResolveMaterial(instance.MaterialIndex);
            if (material != null)
            {
                if (material.ResourceSet != null)
                {
                    cl.SetGraphicsResourceSet(2, material.ResourceSet);
                }

                if (material.ParamsResourceSet != null)
                {
                    cl.SetGraphicsResourceSet(3, material.ParamsResourceSet);
                }
            }
            cl.DrawIndexed((uint)mesh.IndexCount, 1, 0, 0, (uint)i);
        }
        
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
                _meshes.Add(mesh);

                int transformIndex = _modelBuffer.Allocate(worldTransform);
                int instanceIndex = _meshInstances.Count;
                _meshInstances.Add(new MeshInstance(mesh, mesh.MaterialIndex, transformIndex, node));
                _nodeToInstance[node] = instanceIndex;

                if (!_loggedFirstInstance)
                {
                    _loggedFirstInstance = true;
                    LogInstanceDebug(node, node.LocalTransform, worldTransform);
                    Console.WriteLine("[GltfPass] GPU buffer contains:");
                    PrintMatrix(_modelBuffer.GetLastUploadedMatrix());
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
            meshGpu = new MeshGpu(_gd, meshData);
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
            var instance = _meshInstances[instanceIndex];
            if (IsDegenerateMatrix(worldTransform))
            {
                Console.WriteLine($"[GltfPass] Warning: degenerate transform detected for node '{node.Name}', defaulting to identity.");
                worldTransform = Matrix4x4.Identity;
                _nodeWorldTransforms[node] = worldTransform;
            }

            _modelBuffer.UpdateTransform(instance.TransformIndex, worldTransform);
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
        if (allZero)
        {
            return true;
        }

        if (!Matrix4x4.Decompose(matrix, out var scale, out _, out _))
        {
            return true;
        }

        if (Math.Abs(scale.X) < epsilon || Math.Abs(scale.Y) < epsilon || Math.Abs(scale.Z) < epsilon)
        {
            return true;
        }

        return false;
    }

    private static void LogInstanceDebug(SceneNode node, Matrix4x4 local, Matrix4x4 world)
    {
        Console.WriteLine($"[GltfPass] First mesh instance: '{node.Name}'");

        if (Matrix4x4.Decompose(local, out var localScale, out var localRotation, out var localTranslation))
        {
            Console.WriteLine($"  Local  -> Position: {localTranslation}, Scale: {localScale}");
        }
        else
        {
            Console.WriteLine("  Local  -> Decompose failed");
        }

        if (Matrix4x4.Decompose(world, out var worldScale, out var worldRotation, out var worldTranslation))
        {
            Console.WriteLine($"  World  -> Position: {worldTranslation}, Scale: {worldScale}");
        }
        else
        {
            Console.WriteLine("  World  -> Decompose failed");
        }

        Console.WriteLine("  Local matrix:");
        PrintMatrix(local);
        Console.WriteLine("  World matrix:");
        PrintMatrix(world);
    }

    private static void PrintMatrix(Matrix4x4 matrix)
    {
        Console.WriteLine(
            $"    [{matrix.M11,8:F4} {matrix.M12,8:F4} {matrix.M13,8:F4} {matrix.M14,8:F4}]");
        Console.WriteLine(
            $"    [{matrix.M21,8:F4} {matrix.M22,8:F4} {matrix.M23,8:F4} {matrix.M24,8:F4}]");
        Console.WriteLine(
            $"    [{matrix.M31,8:F4} {matrix.M32,8:F4} {matrix.M33,8:F4} {matrix.M34,8:F4}]");
        Console.WriteLine(
            $"    [{matrix.M41,8:F4} {matrix.M42,8:F4} {matrix.M43,8:F4} {matrix.M44,8:F4}]");
    }

    public void Dispose()
    {
        ClearResources();
        _cameraBuffer.Dispose();
        _modelBuffer.Dispose();
        _lightBuffer.Dispose();
        _pipeline.Dispose();
    }
}
