using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Whisperleaf.AssetPipeline.Cache;

/// <summary>
/// Pack/unpack RMA (Roughness, Metallic, AO) textures
/// glTF 2.0 standard: R = AO, G = Roughness, B = Metallic
/// </summary>
public static class RMATexturePacker
{
    /// <summary>
    /// Pack separate textures into RMA format
    /// </summary>
    /// <param name="roughness">Roughness texture (grayscale, goes to G channel)</param>
    /// <param name="metallic">Metallic texture (grayscale, goes to B channel)</param>
    /// <param name="ao">Ambient Occlusion texture (grayscale, goes to R channel), optional</param>
    /// <returns>Packed RMA texture</returns>
    public static Image<Rgba32> Pack(Image<Rgba32>? roughness, Image<Rgba32>? metallic, Image<Rgba32>? ao)
    {
        // Determine output size (use largest input texture)
        int width = 1, height = 1;
        if (roughness != null) { width = Math.Max(width, roughness.Width); height = Math.Max(height, roughness.Height); }
        if (metallic != null) { width = Math.Max(width, metallic.Width); height = Math.Max(height, metallic.Height); }
        if (ao != null) { width = Math.Max(width, ao.Width); height = Math.Max(height, ao.Height); }

        // Resize all inputs to match output size
        Image<Rgba32>? r = ResizeOrDefault(roughness, width, height);
        Image<Rgba32>? m = ResizeOrDefault(metallic, width, height);
        Image<Rgba32>? a = ResizeOrDefault(ao, width, height);

        var packed = new Image<Rgba32>(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Extract values from each channel (use red channel for grayscale textures)
                byte aoValue = 255;      // Default: full AO (no occlusion)
                byte roughnessValue = 128; // Default: mid roughness
                byte metallicValue = 0;   // Default: non-metallic

                if (a != null)
                    aoValue = a[x, y].R;
                if (r != null)
                    roughnessValue = r[x, y].R;
                if (m != null)
                    metallicValue = m[x, y].R;

                // Pack: R = AO, G = Roughness, B = Metallic, A = 255
                packed[x, y] = new Rgba32(aoValue, roughnessValue, metallicValue, 255);
            }
        }

        // Dispose resized images
        if (r != roughness) r?.Dispose();
        if (m != metallic) m?.Dispose();
        if (a != ao) a?.Dispose();

        return packed;
    }

    /// <summary>
    /// Pack from image data that may already be packed
    /// If all three paths point to the same image, it's already packed
    /// </summary>
    public static Image<Rgba32> PackFromPaths(
        string? roughnessPath,
        string? metallicPath,
        string? aoPath,
        Assimp.Scene? scene = null)
    {
        // Check if already packed (all paths point to same embedded texture)
        bool alreadyPacked = !string.IsNullOrEmpty(metallicPath) &&
                            metallicPath == roughnessPath &&
                            metallicPath == aoPath;

        if (alreadyPacked)
        {
            // Already packed, just load it
            var img = LoadTexture(metallicPath, scene);
            if (img != null)
                return img;
        }

        // Load separate textures
        var r = LoadTexture(roughnessPath, scene);
        var m = LoadTexture(metallicPath, scene);
        var a = LoadTexture(aoPath, scene);

        return Pack(r, m, a);
    }

    /// <summary>
    /// Unpack RMA texture into separate channels
    /// </summary>
    public static (Image<Rgba32> ao, Image<Rgba32> roughness, Image<Rgba32> metallic) Unpack(Image<Rgba32> packed)
    {
        int width = packed.Width;
        int height = packed.Height;

        var ao = new Image<Rgba32>(width, height);
        var roughness = new Image<Rgba32>(width, height);
        var metallic = new Image<Rgba32>(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var p = packed[x, y];

                // Unpack: R = AO, G = Roughness, B = Metallic
                ao[x, y] = new Rgba32(p.R, p.R, p.R, 255);
                roughness[x, y] = new Rgba32(p.G, p.G, p.G, 255);
                metallic[x, y] = new Rgba32(p.B, p.B, p.B, 255);
            }
        }

        return (ao, roughness, metallic);
    }

    /// <summary>
    /// Load texture from path (supports embedded glTF textures)
    /// </summary>
    private static Image<Rgba32>? LoadTexture(string? path, Assimp.Scene? scene)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // Embedded texture (starts with *)
        if (path.StartsWith("*") && scene != null && scene.HasTextures)
        {
            if (int.TryParse(path.TrimStart('*'), out int idx) && idx < scene.Textures.Count)
            {
                var texSlot = scene.Textures[idx];
                if (texSlot.IsCompressed && texSlot.CompressedData != null)
                {
                    try
                    {
                        using var ms = new MemoryStream(texSlot.CompressedData);
                        return Image.Load<Rgba32>(ms);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
        }

        // External file
        if (File.Exists(path))
        {
            try
            {
                return Image.Load<Rgba32>(path);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Resize image or create default if null
    /// </summary>
    private static Image<Rgba32>? ResizeOrDefault(Image<Rgba32>? image, int width, int height)
    {
        if (image == null)
            return null;

        if (image.Width == width && image.Height == height)
            return image;

        var resized = image.Clone();
        resized.Mutate(x => x.Resize(width, height));
        return resized;
    }

    /// <summary>
    /// Compute hash for packed RMA texture
    /// This ensures different RMA combinations have different hashes
    /// </summary>
    public static string ComputeRMAHash(Image<Rgba32> packedRMA)
    {
        return WlTexFormat.ComputeHash(packedRMA);
    }
}
