using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Whisperleaf.AssetPipeline.Cache;

/// <summary>
/// Content-addressable asset cache for preprocessed meshes and textures.
/// Uses SHA256 hashing to deduplicate assets and avoid reprocessing.
/// Stores files with friendly names in a scene-based folder structure.
/// </summary>
public static class AssetCache
{
    private static readonly string CacheRoot = Path.Combine(
        ".cache", "whisperleaf"
    );

    private static readonly string RegistryPath = Path.Combine(CacheRoot, "registry.json");

    private static Dictionary<string, CacheEntry>? _registry;
    private static readonly object _lock = new();

    static AssetCache()
    {
        Directory.CreateDirectory(CacheRoot);
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
        lock (_lock)
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
    }

    /// <summary>
    /// Save the asset registry to disk
    /// </summary>
    private static void SaveRegistry()
    {
        lock (_lock)
        {
            if (_registry == null)
                return;

            var json = JsonSerializer.Serialize(_registry, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(RegistryPath, json);
        }
    }

    /// <summary>
    /// Check if a mesh with this hash exists in cache
    /// </summary>
    public static bool HasMesh(string hash, out string path)
    {
        var registry = LoadRegistry();
        lock (_lock)
        {
            if (registry.TryGetValue(hash, out var entry) && entry != null && entry.Type == AssetType.Mesh)
            {
                // Verify file still exists
                if (File.Exists(entry.Path))
                {
                    path = entry.Path;
                    return true;
                }
                else
                {
                    registry.Remove(hash); // Clean up stale entry
                }
            }
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
        lock (_lock)
        {
            if (registry.TryGetValue(hash, out var entry) && entry != null && entry.Type == AssetType.Texture)
            {
                if (File.Exists(entry.Path))
                {
                    path = entry.Path;
                    return true;
                }
                else
                {
                    registry.Remove(hash);
                }
            }
        }

        path = string.Empty;
        return false;
    }

    /// <summary>
    /// Register a new mesh in the cache with a friendly name
    /// </summary>
    public static string RegisterMesh(string hash, MeshCacheMetadata metadata, string sceneName, string meshName)
    {
        // Check if already exists (deduplication)
        if (HasMesh(hash, out string existingPath))
            return existingPath;

        var registry = LoadRegistry();
        
        // Construct preferred path: Cache/SceneName/Meshes/MeshName.wlmesh
        string sanitizedSceneName = SanitizeFileName(sceneName);
        string sanitizedMeshName = SanitizeFileName(meshName);
        if (string.IsNullOrWhiteSpace(sanitizedMeshName)) sanitizedMeshName = "mesh";
        
        string dir = Path.Combine(CacheRoot, sanitizedSceneName, "Meshes");
        dir = EnsureDirectoryPath(dir);

        string filename = $"{sanitizedMeshName}.wlmesh";
        string path = Path.Combine(dir, filename);

        // Handle collision: if file exists but hash is different, append hash
        if (File.Exists(path))
        {
             path = Path.Combine(dir, $"{sanitizedMeshName}_{hash[..8]}.wlmesh");
        }

        Directory.CreateDirectory(dir);

        lock (_lock)
        {
            registry[hash] = new CacheEntry
            {
                Type = AssetType.Mesh,
                Path = path,
                Metadata = metadata
            };
            SaveRegistry();
        }

        return path;
    }

    /// <summary>
    /// Register a new texture in the cache with a friendly name
    /// </summary>
    public static string RegisterTexture(string hash, TextureCacheMetadata metadata, string sceneName, string meshName, string textureName)
    {
        // Check if already exists (deduplication)
        if (HasTexture(hash, out string existingPath))
            return existingPath;

        var registry = LoadRegistry();

        // Construct preferred path: Cache/SceneName/Meshes/MeshName_Textures/TextureName.wltex
        string sanitizedSceneName = SanitizeFileName(sceneName);
        string sanitizedMeshName = SanitizeFileName(meshName);
        if (string.IsNullOrWhiteSpace(sanitizedMeshName)) sanitizedMeshName = "unknown_mesh"; 
        string sanitizedTextureName = SanitizeFileName(textureName);
        if (string.IsNullOrWhiteSpace(sanitizedTextureName)) sanitizedTextureName = "texture";

        string dir = Path.Combine(CacheRoot, sanitizedSceneName, "Meshes", $"{sanitizedMeshName}_Textures");
        dir = EnsureDirectoryPath(dir);

        string filename = $"{sanitizedTextureName}.wltex";
        string path = Path.Combine(dir, filename);

        // Handle collision
        if (File.Exists(path))
        {
            path = Path.Combine(dir, $"{sanitizedTextureName}_{hash[..8]}.wltex");
        }

        Directory.CreateDirectory(dir);

        lock (_lock)
        {
            registry[hash] = new CacheEntry
            {
                Type = AssetType.Texture,
                Path = path,
                Metadata = metadata
            };
            SaveRegistry();
        }

        return path;
    }

    private static string EnsureDirectoryPath(string dir)
    {
        // If a file exists with the same name as the directory we want to create,
        // modify the directory name to avoid collision.
        while (File.Exists(dir))
        {
            dir += "_Dir";
        }
        return dir;
    }

    private static string SanitizeFileName(string name)
    {
        string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        foreach (char c in invalid)
        {
            name = name.Replace(c.ToString(), "");
        }
        name = name.Trim();
        
        // Handle reserved words on Windows
        string[] reserved = { 
            "CON", "PRN", "AUX", "NUL", 
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", 
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" 
        };
        
        if (reserved.Contains(name.ToUpperInvariant()))
        {
            name += "_";
        }

        if (string.IsNullOrWhiteSpace(name))
            return "unnamed";

        return name;
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public static CacheStats GetStats()
    {
        var registry = LoadRegistry();
        lock (_lock)
        {
            return new CacheStats
            {
                TotalEntries = registry.Count,
                MeshCount = registry.Values.Count(e => e.Type == AssetType.Mesh),
                TextureCount = registry.Values.Count(e => e.Type == AssetType.Texture),
                CacheSizeBytes = GetDirectorySize(CacheRoot)
            };
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Clear the entire cache
    /// </summary>
    public static void Clear()
    {
        if (Directory.Exists(CacheRoot))
            Directory.Delete(CacheRoot, true);
        
        Directory.CreateDirectory(CacheRoot);
        
        lock (_lock)
        {
            _registry = new Dictionary<string, CacheEntry>();
            SaveRegistry();
        }
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
