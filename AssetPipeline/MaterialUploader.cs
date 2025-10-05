using Veldrid;
using Veldrid.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
            Console.WriteLine($"  Loading RMA from: {mat.MetallicPath}");
            (mat.MetallicTex, mat.MetallicView) =
                LoadOrDummy(gd, mat.MetallicPath, srgb: false, new RgbaByte(255, (byte)(mat.RoughnessFactor*255), (byte)(mat.MetallicFactor*255), 255), scene);

            Console.WriteLine($"  RMA texture loaded: {mat.MetallicTex.Width}x{mat.MetallicTex.Height}");

            // Create separate views of the same texture for each slot
            // (Some graphics APIs don't like binding the same view to multiple slots)
            mat.RoughnessTex = mat.MetallicTex;
            mat.RoughnessView = factory.CreateTextureView(mat.MetallicTex);
            mat.OcclusionTex = mat.MetallicTex;
            mat.OcclusionView = factory.CreateTextureView(mat.MetallicTex);

            Console.WriteLine($"  DEBUG: After RMA packing - created 3 separate views of same texture");
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
            mat.UsePackedRMA
        );

        // Debug: Log material params
        var bufferSize = System.Runtime.InteropServices.Marshal.SizeOf<MaterialParams>();
        Console.WriteLine($"Uploading MaterialParams for '{mat.Name}':");
        Console.WriteLine($"  Buffer size: {bufferSize} bytes");
        Console.WriteLine($"  BaseColorFactor: {materialParams.BaseColorFactor}");
        Console.WriteLine($"  EmissiveFactor: {materialParams.EmissiveFactor}");
        Console.WriteLine($"  MetallicFactor: {materialParams.MetallicFactor}");
        Console.WriteLine($"  RoughnessFactor: {materialParams.RoughnessFactor}");
        Console.WriteLine($"  UsePackedRMA: {materialParams.UsePackedRMA}");

        mat.ParamsBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)bufferSize,
            BufferUsage.UniformBuffer));
        gd.UpdateBuffer(mat.ParamsBuffer, 0, ref materialParams);

        // DEBUG: Log what we're binding
        Console.WriteLine($"  Creating ResourceSet:");
        Console.WriteLine($"    NormalTex hash: {mat.NormalTex?.GetHashCode()}");
        Console.WriteLine($"    MetallicTex hash: {mat.MetallicTex?.GetHashCode()}");
        Console.WriteLine($"    RoughnessTex hash: {mat.RoughnessTex?.GetHashCode()}");
        Console.WriteLine($"    OcclusionTex hash: {mat.OcclusionTex?.GetHashCode()}");
        Console.WriteLine($"    NormalTex == MetallicTex: {mat.NormalTex == mat.MetallicTex}");

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
            if (path.StartsWith("*") && scene != null && scene.HasTextures)
            {
                if (int.TryParse(path.TrimStart('*'), out int idx) && idx < scene.Textures.Count)
                {
                    Console.WriteLine($"    Loading embedded texture {idx}: IsCompressed={scene.Textures[idx].IsCompressed}, DataSize={scene.Textures[idx].CompressedData?.Length ?? scene.Textures[idx].NonCompressedData?.Length ?? 0}");
                    var factory = gd.ResourceFactory;
                    var texSlot = scene.Textures[idx];
                    if (texSlot.IsCompressed)
                    {
                        try
                        {
                            using var ms = new MemoryStream(texSlot.CompressedData);

                            // DEBUG: Save first RMA texture to disk to inspect
                            if (idx == 1 && path == "*1")
                            {
                                File.WriteAllBytes("/tmp/rma_texture_1.png", texSlot.CompressedData);
                                Console.WriteLine($"    DEBUG: Saved RMA texture to /tmp/rma_texture_1.png");
                            }

                            var img = new ImageSharpTexture(ms, srgb);
                            var tex = img.CreateDeviceTexture(gd, gd.ResourceFactory);
                            var view = gd.ResourceFactory.CreateTextureView(tex);
                            Console.WriteLine($"    Successfully loaded compressed embedded texture {idx}: {tex.Width}x{tex.Height}");
                            return (tex, view);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"    ERROR loading compressed texture {idx}: {ex.Message}");
                            // Fall through to dummy texture
                        }
                    }
                    else
                    {
                        // Non-compressed embedded (raw RGBA as Assimp.Texel[])
                        var texels = texSlot.NonCompressedData;
                        if (texels != null && texels.Length > 0)
                        {
                            byte[] rawBytes = new byte[texels.Length * 4];
                            for (int i = 0; i < texels.Length; i++)
                            {
                                rawBytes[i * 4 + 0] = texels[i].R;
                                rawBytes[i * 4 + 1] = texels[i].G;
                                rawBytes[i * 4 + 2] = texels[i].B;
                                rawBytes[i * 4 + 3] = texels[i].A;
                            }

                            using var ms = new MemoryStream(rawBytes);
                            using var img = Image.Load<Rgba32>(rawBytes); // direct load from byte[] works too
                            using var pngStream = new MemoryStream();
                            img.SaveAsPng(pngStream);
                            pngStream.Position = 0;

                            var ish = new ImageSharpTexture(pngStream, srgb);
                            var tex = ish.CreateDeviceTexture(gd, factory);
                            var view = factory.CreateTextureView(tex);
                            return (tex, view);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"    ERROR: Embedded texture index {idx} out of bounds (scene has {scene.Textures.Count} textures)");
                }
            }


            // External texture
            if (File.Exists(path))
            {
                var img = new ImageSharpTexture(path, srgb);
                var tex = img.CreateDeviceTexture(gd, gd.ResourceFactory);
                var view = gd.ResourceFactory.CreateTextureView(tex);
                return (tex, view);
            }
        }

        // Dummy fallback
        Console.WriteLine($"    Using dummy texture for path: {path ?? "null"}");
        Texture dummy = CreateDummyTexture(gd, fallbackColor);
        TextureView dummyView = gd.ResourceFactory.CreateTextureView(dummy);
        return (dummy, dummyView);
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
}
