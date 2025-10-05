using System.Security.Cryptography;
using System.Text.Json;

namespace Whisperleaf.AssetPipeline.Cache;

/// <summary>
/// Content-addressable asset cache for preprocessed meshes and textures.
/// Uses SHA256 hashing to deduplicate assets and avoid reprocessing.
/// </summary>
public static class AssetCache
{
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "whisperleaf"
    );

    private static readonly string MeshCacheDir = Path.Combine(CacheRoot, "meshes");
    private static readonly string TextureCacheDir = Path.Combine(CacheRoot, "textures");
    private static readonly string RegistryPath = Path.Combine(CacheRoot, "registry.json");

    private static Dictionary<string, CacheEntry>? _registry;

    static AssetCache()
    {
        Directory.CreateDirectory(MeshCacheDir);
        Directory.CreateDirectory(TextureCacheDir);
    }

    /// <summary>
    /// Compute SHA256 hash of byte array
    /// </summary>
    public static string ComputeHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Compute hash of multiple byte arrays concatenated
    /// </summary>
    public static string ComputeHash(params byte[][] dataArrays)
    {
        using var sha256 = SHA256.Create();
        foreach (var data in dataArrays)
        {
            sha256.TransformBlock(data, 0, data.Length, null, 0);
        }
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// Load the asset registry from disk
    /// </summary>
    private static Dictionary<string, CacheEntry> LoadRegistry()
    {
        if (_registry != null)
            return _registry;

        if (!File.Exists(RegistryPath))
        {
            _registry = new Dictionary<string, CacheEntry>();
            return _registry;
        }

        try
        {
            var json = File.ReadAllText(RegistryPath);
            _registry = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json)
                        ?? new Dictionary<string, CacheEntry>();
        }
        catch
        {
            _registry = new Dictionary<string, CacheEntry>();
        }

        return _registry;
    }

    /// <summary>
    /// Save the asset registry to disk
    /// </summary>
    private static void SaveRegistry()
    {
        if (_registry == null)
            return;

        var json = JsonSerializer.Serialize(_registry, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(RegistryPath, json);
    }

    /// <summary>
    /// Check if a mesh with this hash exists in cache
    /// </summary>
    public static bool HasMesh(string hash, out string path)
    {
        var registry = LoadRegistry();
        if (registry.TryGetValue(hash, out var entry) && entry.Type == AssetType.Mesh)
        {
            path = Path.Combine(MeshCacheDir, $"{hash}.wlmesh");
            return File.Exists(path);
        }

        path = string.Empty;
        return false;
    }

    /// <summary>
    /// Check if a texture with this hash exists in cache
    /// </summary>
    public static bool HasTexture(string hash, out string path)
    {
        var registry = LoadRegistry();
        if (registry.TryGetValue(hash, out var entry) && entry.Type == AssetType.Texture)
        {
            path = Path.Combine(TextureCacheDir, $"{hash}.wltex");
            return File.Exists(path);
        }

        path = string.Empty;
        return false;
    }

    /// <summary>
    /// Register a new mesh in the cache
    /// </summary>
    public static string RegisterMesh(string hash, MeshCacheMetadata metadata)
    {
        var registry = LoadRegistry();
        var path = Path.Combine(MeshCacheDir, $"{hash}.wlmesh");

        registry[hash] = new CacheEntry
        {
            Type = AssetType.Mesh,
            Path = path,
            Metadata = metadata
        };

        SaveRegistry();
        return path;
    }

    /// <summary>
    /// Register a new texture in the cache
    /// </summary>
    public static string RegisterTexture(string hash, TextureCacheMetadata metadata)
    {
        var registry = LoadRegistry();
        var path = Path.Combine(TextureCacheDir, $"{hash}.wltex");

        registry[hash] = new CacheEntry
        {
            Type = AssetType.Texture,
            Path = path,
            Metadata = metadata
        };

        SaveRegistry();
        return path;
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public static CacheStats GetStats()
    {
        var registry = LoadRegistry();
        return new CacheStats
        {
            TotalEntries = registry.Count,
            MeshCount = registry.Values.Count(e => e.Type == AssetType.Mesh),
            TextureCount = registry.Values.Count(e => e.Type == AssetType.Texture),
            CacheSizeBytes = GetDirectorySize(CacheRoot)
        };
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        return new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);
    }

    /// <summary>
    /// Clear the entire cache
    /// </summary>
    public static void Clear()
    {
        if (Directory.Exists(MeshCacheDir))
            Directory.Delete(MeshCacheDir, true);
        if (Directory.Exists(TextureCacheDir))
            Directory.Delete(TextureCacheDir, true);
        if (File.Exists(RegistryPath))
            File.Delete(RegistryPath);

        Directory.CreateDirectory(MeshCacheDir);
        Directory.CreateDirectory(TextureCacheDir);
        _registry = new Dictionary<string, CacheEntry>();
        SaveRegistry();
    }
}

public enum AssetType
{
    Mesh,
    Texture
}

public class CacheEntry
{
    public AssetType Type { get; set; }
    public string Path { get; set; } = string.Empty;
    public object? Metadata { get; set; }
}

public class MeshCacheMetadata
{
    public int VertexCount { get; set; }
    public int IndexCount { get; set; }
    public int MaterialIndex { get; set; }
}

public class TextureCacheMetadata
{
    public int Width { get; set; }
    public int Height { get; set; }
    public TextureType TextureType { get; set; }
}

public enum TextureType : uint
{
    BaseColor = 0,
    Normal = 1,
    RMA = 2,
    Emissive = 3
}

public struct CacheStats
{
    public int TotalEntries { get; set; }
    public int MeshCount { get; set; }
    public int TextureCount { get; set; }
    public long CacheSizeBytes { get; set; }

    public override string ToString()
    {
        double sizeMB = CacheSizeBytes / (1024.0 * 1024.0);
        return $"Cache: {TotalEntries} entries ({MeshCount} meshes, {TextureCount} textures), {sizeMB:F2} MB";
    }
}
