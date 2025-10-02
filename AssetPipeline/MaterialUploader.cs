using Veldrid;
using Veldrid.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Whisperleaf.AssetPipeline;
public static class MaterialUploader
{
    public static void Upload(GraphicsDevice gd, ResourceLayout layout, MaterialData mat, Assimp.Scene? scene = null)
    {
        var factory = gd.ResourceFactory;

        (mat.BaseColorTex, mat.BaseColorView) =
            LoadOrDummy(gd, mat.BaseColorPath, srgb: true, new RgbaByte(255,255,255,255), scene);
        (mat.NormalTex, mat.NormalView) =
            LoadOrDummy(gd, mat.NormalPath, srgb: false, new RgbaByte(128,128,255,255), scene);
        (mat.MetallicTex, mat.MetallicView) =
            LoadOrDummy(gd, mat.MetallicPath, srgb: false, new RgbaByte((byte)(mat.MetallicFactor*255),0,0,255), scene);
        (mat.RoughnessTex, mat.RoughnessView) =
            LoadOrDummy(gd, mat.RoughnessPath, srgb: false, new RgbaByte((byte)(mat.RoughnessFactor*255),0,0,255), scene);
        (mat.OcclusionTex, mat.OcclusionView) =
            LoadOrDummy(gd, mat.OcclusionPath, srgb: false, new RgbaByte(255,255,255,255), scene);
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

        mat.ResourceSet = factory.CreateResourceSet(new ResourceSetDescription(layout,
            sampler,
            mat.BaseColorView!,
            mat.NormalView!,
            mat.MetallicView!,
            mat.RoughnessView!,
            mat.OcclusionView!,
            mat.EmissiveView!
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
                    var factory = gd.ResourceFactory;
                    var texSlot = scene.Textures[idx];
                    if (texSlot.IsCompressed)
                    {
                        using var ms = new MemoryStream(texSlot.CompressedData);
                        var img = new ImageSharpTexture(ms, srgb);
                        var tex = img.CreateDeviceTexture(gd, gd.ResourceFactory);
                        var view = gd.ResourceFactory.CreateTextureView(tex);
                        return (tex, view);
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
