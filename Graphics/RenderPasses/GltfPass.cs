using System;
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

    private readonly List<MeshGpu> _meshes = new();
    private readonly List<MaterialData> _materials = new();

    public GltfPass(GraphicsDevice gd)
    {
        _gd = gd;

        _cameraBuffer = new CameraUniformBuffer(gd);

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
            extraLayouts: new[] { _cameraBuffer.Layout, PbrLayout.MaterialLayout }
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

    public void Render(GraphicsDevice gd, CommandList cl, Camera? camera = null)
    {
        if (camera == null || _meshes.Count == 0)
            return;

        _cameraBuffer.Update(gd, camera);

        cl.Begin();
        cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
        cl.ClearColorTarget(0, RgbaFloat.Black);
        cl.ClearDepthStencil(1f);

        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);

        for (int i = 0; i < _meshes.Count; i++)
        {
            var mesh = _meshes[i];

            cl.SetVertexBuffer(0, mesh.VertexBuffer);
            cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);

            var material = ResolveMaterial(mesh.MaterialIndex);
            if (material?.ResourceSet != null)
            {
                cl.SetGraphicsResourceSet(1, material.ResourceSet);
            }

            cl.DrawIndexed((uint)mesh.IndexCount, 1, 0, 0, 0);
        }

        cl.End();
        gd.SubmitCommands(cl);
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
            LoadMeshRecursive(node);
        }
    }

    private void LoadMeshRecursive(SceneNode node)
    {
        if (node.Mesh != null)
        {
            var meshRef = node.Mesh;
            if (TryLoadMesh(meshRef, out MeshGpu? meshGpu))
            {
                _meshes.Add(meshGpu!);
            }
        }

        foreach (var child in node.Children)
        {
            LoadMeshRecursive(child);
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
    }

    public void Dispose()
    {
        ClearResources();
        _cameraBuffer.Dispose();
        _pipeline.Dispose();
    }
}
