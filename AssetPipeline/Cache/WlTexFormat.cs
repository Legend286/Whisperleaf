using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Whisperleaf.AssetPipeline.Cache;

/// <summary>
/// Binary texture format (.wltex) for cached uncompressed textures
/// </summary>
public static class WlTexFormat
{
    private const uint MAGIC = 0x58544C57; // "WLTX" in little-endian
    private const uint VERSION = 1;

    private struct Header
    {
        public uint Magic;
        public uint Version;
        public uint Width;
        public uint Height;
        public TextureType TextureType;
        public uint DataSize;
        public byte[] SourceHash; // 32 bytes SHA256
    }

    /// <summary>
    /// Write texture data to binary format (uncompressed RGBA)
    /// </summary>
    public static void Write(string path, Image<Rgba32> image, TextureType texType, string sourceHash)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Extract pixel data
        byte[] pixels = new byte[image.Width * image.Height * 4];
        int offset = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                pixels[offset++] = pixel.R;
                pixels[offset++] = pixel.G;
                pixels[offset++] = pixel.B;
                pixels[offset++] = pixel.A;
            }
        }

        // Write header
        var hashBytes = Convert.FromHexString(sourceHash);

        bw.Write(MAGIC);
        bw.Write(VERSION);
        bw.Write((uint)image.Width);
        bw.Write((uint)image.Height);
        bw.Write((uint)texType);
        bw.Write((uint)pixels.Length);
        bw.Write(hashBytes, 0, Math.Min(32, hashBytes.Length));

        // Pad hash to 32 bytes if needed
        if (hashBytes.Length < 32)
        {
            bw.Write(new byte[32 - hashBytes.Length]);
        }

        // Write pixel data
        bw.Write(pixels);
    }

    /// <summary>
    /// Read texture from cache
    /// </summary>
    public static Image<Rgba32> Read(string path, out TextureType texType)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        // Read header
        uint magic = br.ReadUInt32();
        uint version = br.ReadUInt32();
        uint width = br.ReadUInt32();
        uint height = br.ReadUInt32();
        texType = (TextureType)br.ReadUInt32();
        uint dataSize = br.ReadUInt32();
        byte[] hash = br.ReadBytes(32);

        if (magic != MAGIC)
            throw new InvalidDataException($"Invalid .wltex file: bad magic number");
        if (version != VERSION)
            throw new InvalidDataException($"Unsupported .wltex version: {version}");

        // Read pixel data
        byte[] pixels = br.ReadBytes((int)dataSize);

        // Create image from pixel data
        var image = new Image<Rgba32>((int)width, (int)height);
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte r = pixels[offset++];
                byte g = pixels[offset++];
                byte b = pixels[offset++];
                byte a = pixels[offset++];
                image[x, y] = new Rgba32(r, g, b, a);
            }
        }

        return image;
    }

    /// <summary>
    /// Compute hash for image data (RGBA pixels)
    /// </summary>
    public static string ComputeHash(Image<Rgba32> image)
    {
        byte[] pixels = new byte[image.Width * image.Height * 4];
        int offset = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                pixels[offset++] = pixel.R;
                pixels[offset++] = pixel.G;
                pixels[offset++] = pixel.B;
                pixels[offset++] = pixel.A;
            }
        }

        return AssetCache.ComputeHash(pixels);
    }
}
