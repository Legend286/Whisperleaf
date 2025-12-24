using Veldrid;
using Veldrid.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.Graphics.Data;
using TextureType = Whisperleaf.AssetPipeline.Cache.TextureType;

namespace Whisperleaf.AssetPipeline;
public static class MaterialUploader
{
    public static void Upload(GraphicsDevice gd, ResourceLayout layout, ResourceLayout paramsLayout, MaterialData mat, Assimp.Scene? scene = null)
    {
        var factory = gd.ResourceFactory;

        (mat.BaseColorRef, mat.BaseColorView) =
            LoadOrDummy(gd, mat.BaseColorPath, srgb: true, new RgbaByte(255,255,255,255), scene);
        (mat.NormalRef, mat.NormalView) =
            LoadOrDummy(gd, mat.NormalPath, srgb: false, new RgbaByte(128,128,255,255), scene);

        if (mat.UsePackedRMA)
        {
            (mat.MetallicRef, mat.MetallicView) =
                LoadOrDummy(gd, mat.MetallicPath, srgb: false, new RgbaByte(255, (byte)(mat.RoughnessFactor*255), (byte)(mat.MetallicFactor*255), 255), scene);

            // Ref Counted sharing: Manually AddRef because we are assigning to multiple fields
            // The first LoadOrDummy returned a ref with count=1 (or incremented if cached).
            // We assign it to MetallicRef.
            // Now we assign to RoughnessRef and OcclusionRef. We must AddRef for each new owner field.
            
            mat.RoughnessRef = mat.MetallicRef;
            mat.RoughnessRef.AddRef();
            mat.RoughnessView = factory.CreateTextureView(mat.MetallicRef.DeviceTexture);
            
            mat.OcclusionRef = mat.MetallicRef;
            mat.OcclusionRef.AddRef();
            mat.OcclusionView = factory.CreateTextureView(mat.MetallicRef.DeviceTexture);
        }
        else
        {
            (mat.MetallicRef, mat.MetallicView) =
                LoadOrDummy(gd, mat.MetallicPath, srgb: false, new RgbaByte(0,0,(byte)(mat.MetallicFactor*255),255), scene);
            (mat.RoughnessRef, mat.RoughnessView) =
                LoadOrDummy(gd, mat.RoughnessPath, srgb: false, new RgbaByte(0,(byte)(mat.RoughnessFactor*255),0,255), scene);
            (mat.OcclusionRef, mat.OcclusionView) =
                LoadOrDummy(gd, mat.OcclusionPath, srgb: false, new RgbaByte(255,255,255,255), scene);
        }
        
        (mat.EmissiveRef, mat.EmissiveView) =
            LoadOrDummy(gd, mat.EmissivePath, srgb: true,
                new RgbaByte(255, 255, 255, 255), scene); // Default to white so factor works

        var sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

        // Create uniform buffer for material parameters
        var materialParams = new MaterialParams(
            mat.BaseColorFactor,
            mat.EmissiveFactor,
            mat.MetallicFactor,
            mat.RoughnessFactor,
            mat.UsePackedRMA,
            mat.AlphaCutoff,
            (int)mat.AlphaMode
        );

        var bufferSize = System.Runtime.InteropServices.Marshal.SizeOf<MaterialParams>();

        mat.ParamsBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)bufferSize,
            BufferUsage.UniformBuffer));
        gd.UpdateBuffer(mat.ParamsBuffer, 0, ref materialParams);

        mat.ResourceSet = factory.CreateResourceSet(new ResourceSetDescription(layout,
            sampler,
            mat.BaseColorView!,
            mat.NormalView!,
            mat.MetallicView!,
            mat.RoughnessView!,
            mat.OcclusionView!,
            mat.EmissiveView!
        ));

        mat.ParamsResourceSet = factory.CreateResourceSet(new ResourceSetDescription(paramsLayout,
            mat.ParamsBuffer
        ));
    }

    private static (RefCountedTexture refTex, TextureView view) LoadOrDummy(GraphicsDevice gd, string? path, bool srgb, RgbaByte fallbackColor, Assimp.Scene? scene)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            // Cached Texture (.wltex)
            if (Path.GetExtension(path).Equals(".wltex", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(path))
                {
                    try
                    {
                        // Returns RefCountedTexture with ref count incremented
                        return CachedTextureUploader.LoadAndUpload(gd, path);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MaterialUploader] ERROR: Failed to load cached texture '{path}': {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"[MaterialUploader] ERROR: Cached texture missing on disk: {path}");
                }
                return CreateDummy(gd, fallbackColor, srgb ? TextureType.BaseColor : TextureType.Normal);
            }

            // External texture (Raw PNG/JPG)
            if (File.Exists(path))
            {
                try 
                {
                    var img = new ImageSharpTexture(path, srgb);
                    var tex = img.CreateDeviceTexture(gd, gd.ResourceFactory);
                    var view = gd.ResourceFactory.CreateTextureView(tex);
                    
                    var refTex = new RefCountedTexture(tex, path, null);
                    return (refTex, view);
                }
                catch (Exception ex)
                {
                     Console.WriteLine($"[MaterialUploader] ERROR: Failed to load external texture '{path}': {ex.Message}");
                }
            }
        }

        // Dummy fallback
        return CreateDummy(gd, fallbackColor, srgb ? TextureType.BaseColor : TextureType.Normal);
    }

    private static (RefCountedTexture refTex, TextureView view) CreateDummy(GraphicsDevice gd, RgbaByte color, TextureType type)
    {
        // Use CachedTextureUploader to create dummy (returns RefCountedTexture)
        // Wait, CachedTextureUploader.CreateDummy returns NEW texture every time.
        // That is fine, it will be ref counted with count=1.
        return CachedTextureUploader.CreateDummy(gd, new Rgba32(color.R, color.G, color.B, color.A), type);
    }
}
