using System;
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

    private readonly List<MeshGpu> _meshes = new();
    private readonly List<MaterialData> _materials = new();
    private readonly List<MeshInstance> _meshInstances = new();

    private readonly struct MeshInstance
    {
        public readonly MeshGpu Mesh;
        public readonly int MaterialIndex;
        public readonly int TransformIndex;

        public MeshInstance(MeshGpu mesh, int materialIndex, int transformIndex)
        {
            Mesh = mesh;
            MaterialIndex = materialIndex;
            TransformIndex = transformIndex;
        }
    }

    public GltfPass(GraphicsDevice gd)
    {
        _gd = gd;

        _cameraBuffer = new CameraUniformBuffer(gd);
        _modelBuffer = new ModelUniformBuffer(gd);

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
            extraLayouts: new[] { _cameraBuffer.Layout, _modelBuffer.Layout, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout }
        );
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

    public void Render(GraphicsDevice gd, CommandList cl, Camera? camera = null)
    {
        if (camera == null || _meshInstances.Count == 0)
            return;

        _cameraBuffer.Update(gd, camera);

        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(1, _modelBuffer.ResourceSet);

        for (int i = 0; i < _meshInstances.Count; i++)
        {
            var instance = _meshInstances[i];
            var mesh = instance.Mesh;

            cl.SetVertexBuffer(0, mesh.VertexBuffer);
            cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
           
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
            cl.DrawIndexed((uint)mesh.IndexCount, 1, 0, 0, (uint)instance.TransformIndex);
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

        foreach (var node in scene.RootNodes)
        {
            LoadMeshRecursive(node, Matrix4x4.Identity);
        }
    }

    private void LoadMeshRecursive(SceneNode node, Matrix4x4 parentWorld)
    {
        var worldTransform = node.LocalTransform * parentWorld;

        if (node.Mesh != null)
        {
            var meshRef = node.Mesh;
            if (TryLoadMesh(meshRef, out MeshGpu? meshGpu))
            {
                var mesh = meshGpu!;
                _meshes.Add(mesh);

                int transformIndex = _modelBuffer.Allocate(worldTransform);
                _meshInstances.Add(new MeshInstance(mesh, mesh.MaterialIndex, transformIndex));
            }
        }

        foreach (var child in node.Children)
        {
            LoadMeshRecursive(child, worldTransform);
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
    }

    public void Dispose()
    {
        ClearResources();
        _cameraBuffer.Dispose();
        _modelBuffer.Dispose();
        _pipeline.Dispose();
    }
}
