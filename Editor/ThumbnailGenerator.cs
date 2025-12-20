using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Veldrid;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics;
using Whisperleaf.Graphics.Data;
using TextureType = Whisperleaf.AssetPipeline.Cache.TextureType;

namespace Whisperleaf.Editor;

public class ThumbnailGenerator : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly EditorManager _editorManager;
    private readonly ResourceFactory _factory;
    private readonly string _cacheDir;
    
    // Loaded GPU textures
    private readonly Dictionary<string, IntPtr> _textures = new();
    private readonly HashSet<string> _pending = new();
    
    // Cache for scene lookups to resolve shared materials
    private readonly Dictionary<string, SceneAsset> _sceneCache = new();
    
    // Rendering Resources
    private Framebuffer _thumbFramebuffer;
    private Texture _thumbColor;
    private Texture _thumbDepth;
    private Texture _readbackTexture;
    private Pipeline _pipeline;
    private DeviceBuffer _uniformBuffer;
    private ResourceSet _uniformSet;
    private ResourceLayout _materialLayout;
    private GeometryBuffer _geoBuffer;
    private CommandList _cl;
    
    private Texture _whiteTex;
    private Texture _normalTex;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbParams
    {
        public Matrix4x4 ViewProjection; // 64
        public Vector3 Color;            // 12
        public float Roughness;          // 4
        public Vector3 CameraPos;        // 12
        public float Metallic;           // 4
    }

    private ConcurrentQueue<(string Path, string OutPath, string Hash)> _modelQueue = new();
    private ConcurrentQueue<(string Path, string Hash)> _loadQueue = new();

    public ThumbnailGenerator(GraphicsDevice gd, EditorManager editorManager)
    {
        _gd = gd;
        _editorManager = editorManager;
        _factory = gd.ResourceFactory;
        _cacheDir = Path.Combine(".cache", "thumbnails");
        Directory.CreateDirectory(_cacheDir);
        
        CreateResources();
    }

    private void CreateResources()
    {
        uint size = 128; 
        
        _thumbColor = _factory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled | TextureUsage.Storage));
            
        _thumbDepth = _factory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil));
            
        _thumbFramebuffer = _factory.CreateFramebuffer(new FramebufferDescription(_thumbDepth, _thumbColor));
        
        _readbackTexture = _factory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
            
        _geoBuffer = new GeometryBuffer(_gd);
        _cl = _factory.CreateCommandList();
        
        // Default Textures
        _whiteTex = CreateSolidTexture(new Rgba32(255,255,255,255));
        _normalTex = CreateSolidTexture(new Rgba32(128, 128, 255, 255));

        // Pipeline
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4),
            new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
        );

        var uniformLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)
        ));
        
        _materialLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BaseColorMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("RMAMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));

        _uniformBuffer = _factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<ThumbParams>(), BufferUsage.UniformBuffer));
        _uniformSet = _factory.CreateResourceSet(new ResourceSetDescription(uniformLayout, _uniformBuffer));

        var vs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/Thumbnail.vert");
        var fs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/Thumbnail.frag");

        var pd = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vertexLayout }, new[] { vs, fs }),
            new[] { uniformLayout, _materialLayout },
            _thumbFramebuffer.OutputDescription
        );

        _pipeline = _factory.CreateGraphicsPipeline(pd);
    }
    
    private Texture CreateSolidTexture(Rgba32 color)
    {
        var tex = _factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        _gd.UpdateTexture(tex, new[] { color.R, color.G, color.B, color.A }, 0, 0, 0, 1, 1, 1, 0, 0);
        return tex;
    }

    public IntPtr GetThumbnail(string path, AssetType type)
    {
        string hash = AssetCache.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path));
        
        if (_textures.TryGetValue(hash, out var id))
            return id;
            
        if (_pending.Contains(hash))
            return IntPtr.Zero; 

        string thumbPath = Path.Combine(_cacheDir, $"{hash}.png");
        
        if (File.Exists(thumbPath))
        {
            LoadTextureToGpu(thumbPath, hash);
            return IntPtr.Zero;
        }
        else
        {
            _pending.Add(hash);
            
            if (type == AssetType.Texture)
            {
                Task.Run(() => GenerateTextureThumbnail(path, thumbPath, hash));
            }
            else
            {
                _modelQueue.Enqueue((path, thumbPath, hash));
            }
            
            return IntPtr.Zero;
        }
    }
    
    public void Update()
    {
        if (_modelQueue.TryDequeue(out var item))
        {
            RenderModelThumbnail(item.Path, item.OutPath, item.Hash);
            if (File.Exists(item.OutPath))
            {
                _loadQueue.Enqueue((item.OutPath, item.Hash));
            }
            else
            {
                _pending.Remove(item.Hash);
            }
        }

        while (_loadQueue.TryDequeue(out var loadItem))
        {
            LoadTextureToGpu(loadItem.Path, loadItem.Hash);
            _pending.Remove(loadItem.Hash);
        }
    }

    private void RenderModelThumbnail(string srcPath, string outPath, string hash)
    {
        List<Texture> tempTextures = new();
        ResourceSet matSet = null;
        var range = default(MeshRange);
        
        try
        {
            var meshData = WlMeshFormat.Read(srcPath, out _);
            range = _geoBuffer.Allocate(meshData.Vertices, meshData.Indices);
            
            // Camera
            var center = (meshData.AABBMin + meshData.AABBMax) * 0.5f;
            var size = meshData.AABBMax - meshData.AABBMin;
            float maxDim = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (maxDim < 0.01f) maxDim = 1.0f;
            
            float dist = maxDim * 1.5f;
            var pos = center + new Vector3(dist * 0.7f, dist * 0.5f, dist * 0.7f);
            var view = Matrix4x4.CreateLookAt(pos, center, Vector3.UnitY);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, 1.0f, 0.1f, 1000.0f);

            // Resolve Material
            Texture baseColor = _whiteTex;
            Texture normalMap = _normalTex;
            Texture rmaMap = _whiteTex;
            
            var matFactors = new Vector3(0.8f);
            float roughness = 0.5f;
            float metallic = 0.0f;

            var scene = GetSceneForMesh(srcPath);
            if (scene != null && meshData.MaterialIndex >= 0 && meshData.MaterialIndex < scene.Materials.Count)
            {
                var mat = scene.Materials[meshData.MaterialIndex];
                matFactors = new Vector3(mat.BaseColorFactor.X, mat.BaseColorFactor.Y, mat.BaseColorFactor.Z);
                roughness = mat.RoughnessFactor;
                metallic = mat.MetallicFactor;

                if (AssetCache.HasTexture(mat.BaseColorHash, out string p1)) { var t = LoadTextureForRender(p1); if(t!=null) { baseColor = t; tempTextures.Add(t); } }
                if (AssetCache.HasTexture(mat.NormalHash, out string p2)) { var t = LoadTextureForRender(p2); if(t!=null) { normalMap = t; tempTextures.Add(t); } }
                if (AssetCache.HasTexture(mat.RMAHash, out string p3)) { var t = LoadTextureForRender(p3); if(t!=null) { rmaMap = t; tempTextures.Add(t); } }
            }
            
            matSet = _factory.CreateResourceSet(new ResourceSetDescription(_materialLayout,
                baseColor, normalMap, rmaMap, _gd.LinearSampler));

            var paramsData = new ThumbParams
            {
                ViewProjection = view * proj,
                Color = matFactors,
                Roughness = roughness,
                CameraPos = pos,
                Metallic = metallic
            };
            
            _cl.Begin();
            _cl.UpdateBuffer(_uniformBuffer, 0, paramsData);
            _cl.SetFramebuffer(_thumbFramebuffer);
            _cl.ClearColorTarget(0, RgbaFloat.Clear);
            _cl.ClearDepthStencil(1.0f);
            
            _cl.SetPipeline(_pipeline);
            _cl.SetGraphicsResourceSet(0, _uniformSet);
            _cl.SetGraphicsResourceSet(1, matSet);
            _cl.SetVertexBuffer(0, _geoBuffer.VertexBuffer);
            _cl.SetIndexBuffer(_geoBuffer.IndexBuffer, IndexFormat.UInt32);
            
            _cl.DrawIndexed(range.IndexCount, 1, range.IndexStart, range.VertexOffset, 0);
            
            _cl.CopyTexture(_thumbColor, _readbackTexture);
            _cl.End();
            
            _gd.SubmitCommands(_cl);
            _gd.WaitForIdle();
            
            var map = _gd.Map(_readbackTexture, MapMode.Read);
            
            using var image = new Image<Rgba32>(128, 128);
            
            unsafe
            {
                byte* srcPtr = (byte*)map.Data;
                for (int y = 0; y < image.Height; y++)
                {
                    byte* rowSrc = srcPtr + (y * map.RowPitch);
                    for (int x = 0; x < image.Width; x++)
                    {
                        byte rVal = rowSrc[x * 4 + 0];
                        byte gVal = rowSrc[x * 4 + 1];
                        byte bVal = rowSrc[x * 4 + 2];
                        byte aVal = rowSrc[x * 4 + 3];
                        image[x, y] = new Rgba32(rVal, gVal, bVal, aVal);
                    }
                }
            }
            
            _gd.Unmap(_readbackTexture);
            image.SaveAsPng(outPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RenderThumb Error: {ex.Message}");
            // Fallback red
            using var img = new Image<Rgba32>(128, 128);
            for(int y=0; y<img.Height; y++) {
                for(int x=0; x<img.Width; x++) {
                    img[x, y] = new Rgba32(255, 0, 0, 255);
                }
            }
            img.SaveAsPng(outPath);
        }
        finally
        {
            if (range.IndexCount > 0) _geoBuffer.Free(range);
            if (matSet != null) matSet.Dispose();
            foreach(var t in tempTextures) t.Dispose();
        }
    }
    
    private SceneAsset? GetSceneForMesh(string meshPath)
    {
        // Extract scene name from cache path
        // .../.cache/whisperleaf/{SceneName}/Meshes/...
        string cacheRoot = Path.GetFullPath(AssetCache.CacheRoot);
        string fullPath = Path.GetFullPath(meshPath);
        if (!fullPath.StartsWith(cacheRoot)) return null;
        
        var relative = Path.GetRelativePath(cacheRoot, fullPath);
        var parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 1) return null;
        
        string sceneName = parts[0];
        
        if (_sceneCache.TryGetValue(sceneName, out var cached)) return cached;
        
        // Find file
        try 
        {
            string[] files = Directory.GetFiles("Resources", $"{sceneName}.wlscene", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                var scene = SceneAsset.Load(files[0]);
                _sceneCache[sceneName] = scene;
                return scene;
            }
        } catch {}
        
        return null;
    }
    
    private Texture? LoadTextureForRender(string path)
    {
        try {
            var img = WlTexFormat.Read(path, out _);
            
            var tex = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)img.Width, (uint)img.Height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
                
            byte[] pixels = new byte[img.Width * img.Height * 4];
            int offset = 0;
            for(int y=0; y<img.Height; y++) {
                for(int x=0; x<img.Width; x++) {
                    var p = img[x, y];
                    pixels[offset++] = p.R; pixels[offset++] = p.G; pixels[offset++] = p.B; pixels[offset++] = p.A;
                }
            }
            
            _gd.UpdateTexture(tex, pixels, 0, 0, 0, (uint)img.Width, (uint)img.Height, 1, 0, 0);
            return tex;
        } catch { return null; }
    }

    private void GenerateTextureThumbnail(string srcPath, string outPath, string hash)
    {
        try
        {
            var image = WlTexFormat.ReadThumbnail(srcPath);
            if (image != null)
            {
                image.SaveAsPng(outPath);
            }
            else
            {
                TextureType type;
                var fullImage = WlTexFormat.Read(srcPath, out type);
                fullImage.Mutate(x => x.Resize(128, 128));
                fullImage.SaveAsPng(outPath);
            }
            
            _loadQueue.Enqueue((outPath, hash));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to gen texture thumb: {ex.Message}");
            _pending.Remove(hash);
        }
    }

    private void LoadTextureToGpu(string path, string hash)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var image = Image.Load<Rgba32>(fs);
            
            var texture = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)image.Width, (uint)image.Height, 1, 1, 
                PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            
            byte[] pixels = new byte[image.Width * image.Height * 4];
            
            int offset = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var p = image[x, y];
                    pixels[offset++] = p.R;
                    pixels[offset++] = p.G;
                    pixels[offset++] = p.B;
                    pixels[offset++] = p.A;
                }
            }
            
            _gd.UpdateTexture(texture, pixels, 0, 0, 0, (uint)image.Width, (uint)image.Height, 1, 0, 0);
            
            var id = _editorManager.GetTextureBinding(texture);
            _textures[hash] = id;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LoadTex Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _thumbColor?.Dispose();
        _thumbDepth?.Dispose();
        _thumbFramebuffer?.Dispose();
        _readbackTexture?.Dispose();
        _pipeline?.Dispose();
        _uniformBuffer?.Dispose();
        _geoBuffer?.Dispose();
        _cl?.Dispose();
        _whiteTex?.Dispose();
        _normalTex?.Dispose();
        _materialLayout?.Dispose();
        _uniformSet?.Dispose();
    }
}