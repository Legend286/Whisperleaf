using Veldrid;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;

namespace Whisperleaf.AssetPipeline.Cache;

/// <summary>
/// Upload cached BC-compressed textures to GPU
/// Generates mipmaps at runtime
/// </summary>
public static class CachedTextureUploader
{
    private static Dictionary<string, RefCountedTexture> _textureCache = new();
    private static object _lock = new();

    /// <summary>
    /// Load cached texture and upload to GPU with mipmaps (Ref Counted)
    /// </summary>
    public static (RefCountedTexture refTex, TextureView view) LoadAndUpload(GraphicsDevice gd, string cachedPath)
    {
        lock (_lock)
        {
            if (_textureCache.TryGetValue(cachedPath, out var existing))
            {
                existing.AddRef();
                // Create a new view for the caller (caller owns view)
                var view = gd.ResourceFactory.CreateTextureView(existing.DeviceTexture);
                return (existing, view);
            }
        }

        // Read uncompressed texture from cache
        using var image = WlTexFormat.Read(cachedPath, out var texType);

        // Determine pixel format
        var pixelFormat = GetPixelFormat(texType);

        // Create texture with mipmaps
        uint mipLevels = CalculateMipLevels(image.Width, image.Height);
        var texture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)image.Width, (uint)image.Height, mipLevels, 1,
            pixelFormat,
            TextureUsage.Sampled | TextureUsage.GenerateMipmaps
        ));
        texture.Name = Path.GetFileName(cachedPath);

        // Upload base mip level
        gd.UpdateTexture(texture, image, 0, 0, 0, (uint)image.Width, (uint)image.Height, 1, 0, 0);

        // Generate mipmaps
        using var cl = gd.ResourceFactory.CreateCommandList();
        cl.Begin();
        cl.GenerateMipmaps(texture);
        cl.End();
        gd.SubmitCommands(cl);
        gd.WaitForIdle();

        var refTex = new RefCountedTexture(texture, cachedPath, () => {
            lock (_lock) { _textureCache.Remove(cachedPath); }
        });
        
        lock (_lock) { _textureCache[cachedPath] = refTex; }

        // Create view
        var view2 = gd.ResourceFactory.CreateTextureView(texture);

        return (refTex, view2);
    }

    /// <summary>
    /// Create dummy texture for missing textures (Ref Counted)
    /// </summary>
    public static (RefCountedTexture refTex, TextureView view) CreateDummy(GraphicsDevice gd, Rgba32 color, TextureType texType)
    {
        // We don't cache dummies globally by path (unless we key by color?), simpler to just create new.
        // Or cache a single dummy?
        // For now, create new.
        
        var pixelFormat = GetPixelFormat(texType);

        var texture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1,
            pixelFormat,
            TextureUsage.Sampled
        ));
        texture.Name = "Dummy_" + texType;

        gd.UpdateTexture(texture, new[] { color }, 0, 0, 0, 1, 1, 1, 0, 0);

        var refTex = new RefCountedTexture(texture, "Dummy", null);
        var view = gd.ResourceFactory.CreateTextureView(texture);
        return (refTex, view);
    }

    /// <summary>
    /// Get Veldrid pixel format for texture type
    /// </summary>
    private static PixelFormat GetPixelFormat(TextureType texType)
    {
        return texType switch
        {
            TextureType.BaseColor => PixelFormat.R8_G8_B8_A8_UNorm_SRgb,
            TextureType.Normal => PixelFormat.R8_G8_B8_A8_UNorm,
            TextureType.RMA => PixelFormat.R8_G8_B8_A8_UNorm,
            TextureType.Emissive => PixelFormat.R8_G8_B8_A8_UNorm_SRgb, // Emissive is sRGB
            _ => PixelFormat.R8_G8_B8_A8_UNorm
        };
    }

    /// <summary>
    /// Calculate number of mip levels for texture size
    /// </summary>
    private static uint CalculateMipLevels(int width, int height)
    {
        uint levels = 1;
        int size = Math.Max(width, height);

        while (size > 1)
        {
            size /= 2;
            levels++;
        }

        return levels;
    }

    /// <summary>
    /// Helper to convert ImageSharp image to byte array and upload
    /// </summary>
    private static void UpdateTexture(this GraphicsDevice gd, Texture texture, Image<Rgba32> image,
        uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer)
    {
        // Convert image to byte array
        byte[] pixels = new byte[image.Width * image.Height * 4];
        int offset = 0;
        for (int row = 0; row < image.Height; row++)
        {
            for (int col = 0; col < image.Width; col++)
            {
                var pixel = image[col, row];
                pixels[offset++] = pixel.R;
                pixels[offset++] = pixel.G;
                pixels[offset++] = pixel.B;
                pixels[offset++] = pixel.A;
            }
        }

        gd.UpdateTexture(texture, pixels, x, y, z, width, height, depth, mipLevel, arrayLayer);
    }
}
