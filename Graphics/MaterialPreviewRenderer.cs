using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.Assets;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics;

public class MaterialPreviewRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    
    private Framebuffer _framebuffer;
    private Texture _colorTarget;
    private Texture _depthTarget;
    
    // Shadow resources
    private Framebuffer _shadowFramebuffer;
    private Texture _shadowMap;
    private ResourceSet _shadowResourceSet;
    private ResourceLayout _shadowLayout;
    
    // Preview Resources
    private Pipeline _previewPipeline;
    private Pipeline _shadowPipeline;
    private DeviceBuffer _orthoLightBuffer;
    private ResourceSet _orthoLightResourceSet;
    private ResourceLayout _orthoLightLayout;
    
    private readonly Camera _camera;
    private readonly CameraUniformBuffer _cameraBuffer;
    private readonly ModelUniformBuffer _modelBuffer;
    private readonly GeometryBuffer _geometryBuffer;
    private SkyboxPass _skyPass;
    
    private SceneNode _previewNode;
    private float _rotation;

    [StructLayout(LayoutKind.Sequential)]
    private struct OrthoLight
    {
        public Matrix4x4 ViewProj;
        public Matrix4x4 ShadowViewProj;
        public Vector4 Direction; // xyz = dir, w = intensity
        public Vector4 Color; // xyz = color
    }

    public Camera Camera => _camera;
    public uint Width => _colorTarget?.Width ?? 0;
    public uint Height => _colorTarget?.Height ?? 0;

    public MaterialPreviewRenderer(GraphicsDevice gd, Renderer renderer)
    {
        _gd = gd;
        var factory = gd.ResourceFactory;

        _cameraBuffer = new CameraUniformBuffer(gd);
        _modelBuffer = new ModelUniformBuffer(gd);
        _geometryBuffer = new GeometryBuffer(gd);
        _skyPass = null; // Initialized in Resize

        // Setup Camera
        _camera = new Camera(1.0f);
        _camera.Position = new Vector3(0, 1.5f, 4.0f);
        _camera.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.3f);

        CreateGraphicsResources();
        CreateMeshes();
        Resize(512, 512);
    }

    private void CreateGraphicsResources()
    {
        var factory = _gd.ResourceFactory;

        // Ortho Light Buffer & Layout
        _orthoLightBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<OrthoLight>(), BufferUsage.UniformBuffer));
        _orthoLightLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("OrthoLight", ResourceKind.UniformBuffer, ShaderStages.Fragment | ShaderStages.Vertex)
        ));
        _orthoLightResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_orthoLightLayout, _orthoLightBuffer));

        // Shadow Map
        _shadowMap = factory.CreateTexture(TextureDescription.Texture2D(2048, 2048, 1, 1, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil | TextureUsage.Sampled));
        _shadowFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(_shadowMap, Array.Empty<Texture>()));
        
        var shadowSampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear, ComparisonKind.LessEqual,
            0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

        _shadowLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ShadowMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));
        _shadowResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_shadowLayout, _shadowMap, shadowSampler));

        // Pipelines
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4),
            new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
        );

        var outputDesc = new OutputDescription(
            new OutputAttachmentDescription(PixelFormat.D32_Float_S8_UInt),
            new OutputAttachmentDescription(PixelFormat.R8_G8_B8_A8_UNorm)
        );

        _previewPipeline = PipelineFactory.CreatePipeline(
            _gd, "Graphics/Shaders/Preview.vert", "Graphics/Shaders/Preview.frag",
            vertexLayout, outputDesc, true, false,
            new[] { _cameraBuffer.Layout, _modelBuffer.Layout, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, _orthoLightLayout, _shadowLayout },
            true);

        _shadowPipeline = PipelineFactory.CreatePipeline(
            _gd, "Graphics/Shaders/PreviewShadow.vert", "Graphics/Shaders/Shadow.frag",
            vertexLayout, _shadowFramebuffer.OutputDescription, true, false,
            new[] { _orthoLightLayout, _modelBuffer.Layout }, // Set 0 = OrthoLight
            true);
    }

    private MeshGpu _previewMesh;
    private MeshGpu _groundMesh;
    private MaterialData _previewMaterial;
    private MaterialData _groundMaterial;

    private void CreateMeshes()
    {
        _previewMesh = new MeshGpu(_geometryBuffer, PrimitiveGenerator.CreateSphere(1.0f, 32));
        _groundMesh = new MeshGpu(_geometryBuffer, PrimitiveGenerator.CreatePlane(20.0f, 20.0f));
        
        _previewMaterial = new MaterialData { Name = "PreviewMat" };
        MaterialUploader.Upload(_gd, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, _previewMaterial);
        
        // Generate Checkerboard textures
        var (albedo, rma) = GenerateCheckerboard();
        
        _groundMaterial = new MaterialData { 
            Name = "GroundMat",
            BaseColorImage = albedo,
            MetallicImage = rma,
            UsePackedRMA = true
        };
        MaterialUploader.Upload(_gd, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, _groundMaterial);
    }

    private (Image<Rgba32> albedo, Image<Rgba32> rma) GenerateCheckerboard()
    {
        int size = 512;
        int grid = 8;
        var albedo = new Image<Rgba32>(size, size);
        var rma = new Image<Rgba32>(size, size);
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool isLight = ((x * grid / size) + (y * grid / size)) % 2 == 0;
                
                // Albedo: Light grey (0.8) and Medium grey (0.4)
                byte albedoVal = isLight ? (byte)204 : (byte)102;
                albedo[x, y] = new Rgba32(albedoVal, albedoVal, albedoVal, 255);
                
                // RMA: R=AO (1.0), G=Roughness, B=Metallic (0.0)
                // Light grey grid (isLight) = 0.2 rough
                // Medium grey grid (!isLight) = 0.5 rough
                byte roughVal = isLight ? (byte)51 : (byte)127;
                rma[x, y] = new Rgba32(255, roughVal, 0, 255);
            }
        }
        
        return (albedo, rma);
    }

    public void Resize(uint width, uint height)
    {
        if (_colorTarget != null && _colorTarget.Width == width && _colorTarget.Height == height) return;
        if (width == 0 || height == 0) return;

        _gd.WaitForIdle();
        _colorTarget?.Dispose();
        _depthTarget?.Dispose();
        _framebuffer?.Dispose();

        var factory = _gd.ResourceFactory;
        _colorTarget = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _depthTarget = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil));
        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTarget, _colorTarget));
        
        _skyPass?.Dispose();
        _skyPass = new SkyboxPass(_gd, _cameraBuffer, _framebuffer.OutputDescription);
        
        _camera.AspectRatio = (float)width / height;
    }

    public void UpdateMaterial(MaterialAsset asset)
    {
        // Simple dirty check to avoid redundant uploads
        if (_previewMaterial.Name == asset.Name &&
            _previewMaterial.BaseColorFactor == asset.BaseColorFactor &&
            _previewMaterial.EmissiveFactor == asset.EmissiveFactor &&
            _previewMaterial.MetallicFactor == asset.MetallicFactor &&
            _previewMaterial.RoughnessFactor == asset.RoughnessFactor &&
            _previewMaterial.AlphaMode == asset.AlphaMode &&
            _previewMaterial.AlphaCutoff == asset.AlphaCutoff &&
            _previewMaterial.BaseColorPath == asset.BaseColorTexture &&
            _previewMaterial.NormalPath == asset.NormalTexture &&
            _previewMaterial.EmissivePath == asset.EmissiveTexture &&
            _previewMaterial.MetallicPath == asset.RMATexture)
        {
            return;
        }

        _gd.WaitForIdle();
        _previewMaterial.Dispose(); // Dispose old GPU resources before re-uploading

        _previewMaterial.Name = asset.Name;
        _previewMaterial.BaseColorFactor = asset.BaseColorFactor;
        _previewMaterial.EmissiveFactor = asset.EmissiveFactor;
        _previewMaterial.MetallicFactor = asset.MetallicFactor;
        _previewMaterial.RoughnessFactor = asset.RoughnessFactor;
        _previewMaterial.AlphaMode = asset.AlphaMode;
        _previewMaterial.AlphaCutoff = asset.AlphaCutoff;
        _previewMaterial.BaseColorPath = asset.BaseColorTexture;
        _previewMaterial.NormalPath = asset.NormalTexture;
        _previewMaterial.EmissivePath = asset.EmissiveTexture;
        _previewMaterial.MetallicPath = asset.RMATexture;
        _previewMaterial.UsePackedRMA = !string.IsNullOrEmpty(asset.RMATexture);

        MaterialUploader.Upload(_gd, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, _previewMaterial);
    }

    public void SetPreviewMesh(string meshPath)
    {
        try {
            Console.WriteLine($"[MaterialPreview] Setting mesh: {meshPath}");
            var data = WlMeshFormat.Read(meshPath, out string hash);
            
            _gd.WaitForIdle();
            _previewMesh?.Dispose();
            _previewMesh = new MeshGpu(_geometryBuffer, data);
            
            // Look up material in registry via mesh hash
            if (AssetCache.TryGetMeshMetadata(hash, out var meta) && !string.IsNullOrEmpty(meta.MaterialPath))
            {
                if (File.Exists(meta.MaterialPath))
                {
                    var asset = MaterialAsset.Load(meta.MaterialPath);
                    UpdateMaterial(asset);
                }
            }
        } catch (Exception ex) { Console.WriteLine($"[Preview] Mesh load failed: {ex.Message}"); }
    }

    public void Update(float dt)
    {
        _rotation += dt * 0.2f;
        _cameraBuffer.Update(_gd, _camera, new Vector2(Width, Height), 0);
        
        // Update Light
        var sunDir = Vector3.Normalize(new Vector3(1, -1, -1));
        
        // 1. Lighting bounds (Scene wide, covers 20x20 plane)
        var lightView = Matrix4x4.CreateLookAt(-sunDir * 15.0f, Vector3.Zero, Vector3.UnitY);
        var lightProj = Matrix4x4.CreateOrthographic(25.0f, 25.0f, 0.1f, 40.0f);
        
        // 2. Tighter shadow bounds (Fits mesh only)
        UpdateShadowBounds(Matrix4x4.CreateRotationY(_rotation), sunDir, out var sView, out var sProj);
        
        var light = new OrthoLight {
            ViewProj = lightView * lightProj,
            ShadowViewProj = sView * sProj,
            Direction = new Vector4(sunDir, 5.0f),
            Color = new Vector4(1, 1, 1, 1)
        };
        _gd.UpdateBuffer(_orthoLightBuffer, 0, ref light);
        _skyPass?.UpdateSun(-sunDir);
    }

    private void UpdateShadowBounds(Matrix4x4 rotation, Vector3 sunDir, out Matrix4x4 view, out Matrix4x4 proj)
    {
        if (_previewMesh == null) { view = Matrix4x4.Identity; proj = Matrix4x4.Identity; return; }

        var min = _previewMesh.AABBMin; var max = _previewMesh.AABBMax;
        Vector3[] corners = {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z)
        };

        Vector3 center = (min + max) * 0.5f;
        center = Vector3.Transform(center, rotation);
        view = Matrix4x4.CreateLookAt(center - sunDir * 10.0f, center, Vector3.UnitY);

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        foreach (var c in corners) {
            var worldC = Vector3.Transform(c, rotation);
            var lightC = Vector3.Transform(worldC, view);
            minX = Math.Min(minX, lightC.X); minY = Math.Min(minY, lightC.Y); minZ = Math.Min(minZ, lightC.Z);
            maxX = Math.Max(maxX, lightC.X); maxY = Math.Max(maxY, lightC.Y); maxZ = Math.Max(maxZ, lightC.Z);
        }

        float pad = 0.1f;
        proj = Matrix4x4.CreateOrthographicOffCenter(minX - pad, maxX + pad, minY - pad, maxY + pad, 0.1f, 20.0f);
    }

    public void Render(CommandList cl)
    {
        if (_framebuffer == null) return;
        if (_previewMesh == null) CreateMeshes();

        // 1. Shadow Pass
        cl.SetFramebuffer(_shadowFramebuffer);
        cl.ClearDepthStencil(1.0f);
        cl.SetPipeline(_shadowPipeline);
        cl.SetGraphicsResourceSet(0, _orthoLightResourceSet);
        
        // Draw Preview Object into Shadow map using tight frustum
        _modelBuffer.Clear();
        int previewIdx = _modelBuffer.Allocate(Matrix4x4.CreateRotationY(_rotation));
        cl.SetGraphicsResourceSet(1, _modelBuffer.ResourceSet);
        cl.SetVertexBuffer(0, _previewMesh.VertexBuffer);
        cl.SetIndexBuffer(_previewMesh.IndexBuffer, IndexFormat.UInt32);
        cl.DrawIndexed((uint)_previewMesh.IndexCount, 1, _previewMesh.Range.IndexStart, (int)_previewMesh.Range.VertexOffset, (uint)previewIdx);

        // 2. Main Pass
        cl.SetFramebuffer(_framebuffer);
        cl.ClearColorTarget(0, RgbaFloat.Black);
        cl.ClearDepthStencil(1.0f);
        
        // Sky
        _skyPass?.Render(_gd, cl, _camera, new Vector2(Width, Height), 0);

        cl.SetPipeline(_previewPipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(4, _orthoLightResourceSet);
        cl.SetGraphicsResourceSet(5, _shadowResourceSet);

        // Draw Ground
        int groundIdx = _modelBuffer.Allocate(Matrix4x4.CreateTranslation(0, -1.0f, 0));
        cl.SetGraphicsResourceSet(1, _modelBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(2, _groundMaterial.ResourceSet);
        cl.SetGraphicsResourceSet(3, _groundMaterial.ParamsResourceSet);
        cl.SetVertexBuffer(0, _groundMesh.VertexBuffer);
        cl.SetIndexBuffer(_groundMesh.IndexBuffer, IndexFormat.UInt32);
        cl.DrawIndexed((uint)_groundMesh.IndexCount, 1, _groundMesh.Range.IndexStart, (int)_groundMesh.Range.VertexOffset, (uint)groundIdx);

        // Draw Preview Object
        cl.SetGraphicsResourceSet(1, _modelBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(2, _previewMaterial.ResourceSet);
        cl.SetGraphicsResourceSet(3, _previewMaterial.ParamsResourceSet);
        cl.SetVertexBuffer(0, _previewMesh.VertexBuffer);
        cl.SetIndexBuffer(_previewMesh.IndexBuffer, IndexFormat.UInt32);
        cl.DrawIndexed((uint)_previewMesh.IndexCount, 1, _previewMesh.Range.IndexStart, (int)_previewMesh.Range.VertexOffset, (uint)previewIdx);
    }

    public Texture GetTexture() => _colorTarget;

    public void Dispose()
    {
        _framebuffer?.Dispose();
        _colorTarget?.Dispose();
        _depthTarget?.Dispose();
        _shadowFramebuffer?.Dispose();
        _shadowMap?.Dispose();
        _orthoLightBuffer?.Dispose();
        _previewMesh?.Dispose();
        _groundMesh?.Dispose();
        _previewMaterial?.Dispose();
        _groundMaterial?.Dispose();
        _cameraBuffer.Dispose();
        _modelBuffer.Dispose();
        _geometryBuffer.Dispose();
        _skyPass?.Dispose();
    }
}