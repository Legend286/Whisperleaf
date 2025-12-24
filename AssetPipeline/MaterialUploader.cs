using Veldrid;
using Veldrid.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.Graphics.Data;

namespace Whisperleaf.AssetPipeline;
public static class MaterialUploader
{
    public static void Upload(GraphicsDevice gd, ResourceLayout layout, ResourceLayout paramsLayout, MaterialData mat, Assimp.Scene? scene = null)
    {
        var factory = gd.ResourceFactory;

        (mat.BaseColorTex, mat.BaseColorView) =
            LoadOrDummy(gd, mat.BaseColorPath, srgb: true, new RgbaByte(255,255,255,255), scene);
        (mat.NormalTex, mat.NormalView) =
            LoadOrDummy(gd, mat.NormalPath, srgb: false, new RgbaByte(128,128,255,255), scene);

        if (mat.UsePackedRMA)
        {
            // Load the packed RMA texture once and reuse for all three slots
            // glTF standard: R=AO (default 255), G=Roughness, B=Metallic
          //  Console.WriteLine($"  Loading RMA from: {mat.MetallicPath}");
            (mat.MetallicTex, mat.MetallicView) =
                LoadOrDummy(gd, mat.MetallicPath, srgb: false, new RgbaByte(255, (byte)(mat.RoughnessFactor*255), (byte)(mat.MetallicFactor*255), 255), scene);

           // Console.WriteLine($"  RMA texture loaded: {mat.MetallicTex.Width}x{mat.MetallicTex.Height}");

            // Create separate views of the same texture for each slot
            // (Some graphics APIs don't like binding the same view to multiple slots)
            mat.RoughnessTex = mat.MetallicTex;
            mat.RoughnessView = factory.CreateTextureView(mat.MetallicTex);
            mat.OcclusionTex = mat.MetallicTex;
            mat.OcclusionView = factory.CreateTextureView(mat.MetallicTex);

         //   Console.WriteLine($"  DEBUG: After RMA packing - created 3 separate views of same texture");
        }
        else
        {
            // Load separate textures
            (mat.MetallicTex, mat.MetallicView) =
                LoadOrDummy(gd, mat.MetallicPath, srgb: false, new RgbaByte(0,0,(byte)(mat.MetallicFactor*255),255), scene);
            (mat.RoughnessTex, mat.RoughnessView) =
                LoadOrDummy(gd, mat.RoughnessPath, srgb: false, new RgbaByte(0,(byte)(mat.RoughnessFactor*255),0,255), scene);
            (mat.OcclusionTex, mat.OcclusionView) =
                LoadOrDummy(gd, mat.OcclusionPath, srgb: false, new RgbaByte(255,255,255,255), scene);
        }
        (mat.EmissiveTex, mat.EmissiveView) =
            LoadOrDummy(gd, mat.EmissivePath, srgb: true,
                new RgbaByte(
                    (byte)(mat.EmissiveFactor.X*255),
                    (byte)(mat.EmissiveFactor.Y*255),
                    (byte)(mat.EmissiveFactor.Z*255),
                    255), scene);

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

        // Debug: Log material params
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

    private static (Texture tex, TextureView view) LoadOrDummy(GraphicsDevice gd, string? path, bool srgb, RgbaByte fallbackColor, Assimp.Scene? scene)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (Path.GetExtension(path).Equals(".wltex", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var result = CachedTextureUploader.LoadAndUpload(gd, path);
                        // Console.WriteLine($"[MaterialUploader] Loaded cached texture: {Path.GetFileName(path)}");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MaterialUploader] ERROR: Failed to load cached texture '{path}': {ex.Message}");
                        return CreateDummyTextureWithView(gd, fallbackColor);
                    }
                }

                Console.WriteLine($"[MaterialUploader] ERROR: Cached texture missing on disk: {path}");
                return CreateDummyTextureWithView(gd, fallbackColor);
            }

            if (path.StartsWith("*") && scene != null && scene.HasTextures)
            {
                 // ... (Keep existing logic for embedded textures, but maybe add logging on error)
                 // ...
            }
            // ...
            
            // External texture
            if (File.Exists(path))
            {
                try 
                {
                    var img = new ImageSharpTexture(path, srgb);
                    var tex = img.CreateDeviceTexture(gd, gd.ResourceFactory);
                    var view = gd.ResourceFactory.CreateTextureView(tex);
                    // Console.WriteLine($"[MaterialUploader] Loaded external texture: {path}");
                    return (tex, view);
                }
                catch (Exception ex)
                {
                     Console.WriteLine($"[MaterialUploader] ERROR: Failed to load external texture '{path}': {ex.Message}");
                     return CreateDummyTextureWithView(gd, fallbackColor);
                }
            }
            else 
            {
                 Console.WriteLine($"[MaterialUploader] ERROR: External texture missing: {path}");
            }
        }
        else
        {
            // Only log if it's not an optional texture (hard to know here, but useful for debug)
            // Console.WriteLine($"[MaterialUploader] Texture path is null/empty. Using dummy.");
        }

        // Dummy fallback
        return CreateDummyTextureWithView(gd, fallbackColor);
    }

    private static Texture CreateDummyTexture(GraphicsDevice gd, RgbaByte color)
    {
        var tex = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled));
        gd.UpdateTexture(tex, new[] { color }, 0, 0, 0, 1, 1, 1, 0, 0);
        return tex;
    }

    private static (Texture tex, TextureView view) CreateDummyTextureWithView(GraphicsDevice gd, RgbaByte color)
    {
        Texture dummy = CreateDummyTexture(gd, color);
        TextureView dummyView = gd.ResourceFactory.CreateTextureView(dummy);
        return (dummy, dummyView);
    }
}
