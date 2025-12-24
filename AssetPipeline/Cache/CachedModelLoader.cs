using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Whisperleaf.Graphics.Loaders;

namespace Whisperleaf.AssetPipeline.Cache;

/// <summary>
/// Model loader with content-addressable caching
/// Processes glTF files and caches preprocessed meshes and textures
/// </summary>
public static class CachedModelLoader
{
    /// <summary>
    /// Load model from glTF file with caching
    /// Returns cached assets if available, otherwise processes and caches them
    /// </summary>
    public static (List<MeshData> meshes, List<MaterialData> materials, Assimp.Scene scene) Load(string path, string sceneName)
    {
        // Always load the source file with Assimp to get the scene structure
        var (sourceMeshes, sourceMaterials, scene) = AssimpLoader.LoadCPU(path);

        // Build a map of material index to the first mesh that uses it, for texture organization
        var materialToMeshMap = new Dictionary<int, string>();
        for (int i = 0; i < sourceMeshes.Count; i++)
        {
            var mesh = sourceMeshes[i];
            if (!materialToMeshMap.ContainsKey(mesh.MaterialIndex))
            {
                materialToMeshMap[mesh.MaterialIndex] = string.IsNullOrWhiteSpace(mesh.Name) ? $"mesh_{i}" : mesh.Name;
            }
        }

        // Process meshes (check cache or process)
        var cachedMeshes = new List<MeshData>();
        foreach (var mesh in sourceMeshes)
        {
            // Center the mesh geometry to improve instancing efficiency
            // Two identical meshes at different locations will become identical after centering
            Vector3 center = (mesh.AABBMin + mesh.AABBMax) * 0.5f;
            ApplyCentering(mesh, center);
            mesh.CenteringOffset = center;

            var meshHash = WlMeshFormat.ComputeHash(mesh);

            if (AssetCache.HasMesh(meshHash, out string meshPath))
            {
                var cached = WlMeshFormat.Read(meshPath, out _);
                cached.Name = mesh.Name;
                cached.WorldMatrix = mesh.WorldMatrix;
                cached.CenteringOffset = center; // Pass the offset to the importer
                cached.MaterialIndex = mesh.MaterialIndex; // Restore original material index
                cachedMeshes.Add(cached);
            }
            else
            {
                meshPath = AssetCache.RegisterMesh(meshHash, new MeshCacheMetadata
                {
                    VertexCount = mesh.Vertices.Length / 12,
                    IndexCount = mesh.Indices.Length,
                    MaterialIndex = mesh.MaterialIndex
                }, sceneName, mesh.Name);

                WlMeshFormat.Write(meshPath, mesh, meshHash);
                cachedMeshes.Add(mesh);
            }
        }

        // Process materials (check cache or process)
        var cachedMaterials = new List<MaterialData>();
        for (int i = 0; i < sourceMaterials.Count; i++)
        {
            var mat = sourceMaterials[i];
            string owningMeshName = materialToMeshMap.TryGetValue(i, out string? name) ? name : "unassigned_material";

            (mat.BaseColorPath, mat.BaseColorHash) = ProcessTexture(mat.BaseColorPath, TextureType.BaseColor, scene, sceneName, owningMeshName);
            (mat.NormalPath, mat.NormalHash) = ProcessTexture(mat.NormalPath, TextureType.Normal, scene, sceneName, owningMeshName);
            (mat.EmissivePath, mat.EmissiveHash) = ProcessTexture(mat.EmissivePath, TextureType.Emissive, scene, sceneName, owningMeshName);

            (mat.MetallicPath, mat.RMAHash) = ProcessRMATexture(mat.RoughnessPath, mat.MetallicPath, mat.OcclusionPath, scene, sceneName, owningMeshName);
            mat.RoughnessTex = null;
            mat.OcclusionPath = null;
            mat.UsePackedRMA = true;

            // Generate and Save .wlmat
            try 
            {
                string safeMatName = string.Join("_", mat.Name.Split(Path.GetInvalidFileNameChars()));
                if (string.IsNullOrWhiteSpace(safeMatName)) safeMatName = "unnamed";
                
                string fileName = $"Material_{i}_{safeMatName}.wlmat";
                
                // Determine directory: prefer same as textures
                string? dir = null;
                if (!string.IsNullOrEmpty(mat.BaseColorPath)) dir = Path.GetDirectoryName(mat.BaseColorPath);
                
                // Fallback
                if (string.IsNullOrEmpty(dir)) {
                     dir = Path.Combine(AssetCache.CacheRoot, sceneName, "meshes", owningMeshName);
                }
                
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string fullPath = Path.Combine(dir, fileName);
                
                var asset = new MaterialAsset {
                    Name = mat.Name,
                    BaseColorFactor = mat.BaseColorFactor,
                    EmissiveFactor = mat.EmissiveFactor,
                    MetallicFactor = mat.MetallicFactor,
                    RoughnessFactor = mat.RoughnessFactor,
                    AlphaMode = AlphaMode.Opaque,
                    AlphaCutoff = 0.5f,
                    BaseColorTexture = mat.BaseColorPath,
                    NormalTexture = mat.NormalPath,
                    EmissiveTexture = mat.EmissivePath,
                    RMATexture = mat.MetallicPath
                };
                
                if (mat.BaseColorFactor.W < 0.99f) asset.AlphaMode = AlphaMode.Blend;
                if (mat.Name.Contains("leaf", StringComparison.OrdinalIgnoreCase) || mat.Name.Contains("foliage", StringComparison.OrdinalIgnoreCase))
                {
                    asset.AlphaMode = AlphaMode.Mask;
                }

                asset.Save(fullPath);
                mat.AssetPath = fullPath;
                mat.AlphaMode = asset.AlphaMode; // Sync runtime data
                
                Console.WriteLine($"[CachedModelLoader] Saved material: {fullPath}");
            } 
            catch (Exception ex) 
            {
                Console.WriteLine($"[CachedModelLoader] Error saving .wlmat: {ex.Message}");
            }

            cachedMaterials.Add(mat);
        }

        return (cachedMeshes, cachedMaterials, scene);
    }

    private static void ApplyCentering(MeshData mesh, Vector3 offset)
    {
        for (int i = 0; i < mesh.Vertices.Length; i += 12)
        {
            mesh.Vertices[i] -= offset.X;
            mesh.Vertices[i + 1] -= offset.Y;
            mesh.Vertices[i + 2] -= offset.Z;
        }

        mesh.AABBMin -= offset;
        mesh.AABBMax -= offset;
    }

    /// <summary>
    /// Process a single texture (check cache or compress and cache)
    /// </summary>
    private static (string? path, string? hash) ProcessTexture(string? sourcePath, TextureType textureType, Assimp.Scene? scene, string sceneName, string meshName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            // Console.WriteLine($"[CachedModelLoader] Skipping {textureType} for {meshName}: source path is empty");
            return (null, null);
        }

        Image<Rgba32>? sourceImage = LoadSourceImage(sourcePath, scene);
        if (sourceImage == null)
        {
            Console.WriteLine($"[CachedModelLoader] ERROR: Failed to load source image for {textureType} (Path: {sourcePath})");
            return (null, null);
        }

        using (sourceImage)
        {
            var hash = WlTexFormat.ComputeHash(sourceImage);

            if (AssetCache.HasTexture(hash, out string cachedPath))
            {
               // Console.WriteLine($"[CachedModelLoader] Found cached {textureType}: {cachedPath}");
                return (cachedPath, hash);
            }

            string friendlyName = GetFriendlyTextureName(sourcePath, textureType);
            
            cachedPath = AssetCache.RegisterTexture(hash, new TextureCacheMetadata
            {
                Width = sourceImage.Width,
                Height = sourceImage.Height,
                TextureType = textureType
            }, sceneName, meshName, friendlyName);

            WlTexFormat.Write(cachedPath, sourceImage, textureType, hash);
            Console.WriteLine($"[CachedModelLoader] Saved {textureType}: {cachedPath}");
            return (cachedPath, hash);
        }
    }

    private static string GetFriendlyTextureName(string sourcePath, TextureType type)
    {
        if (sourcePath.StartsWith("*"))
        {
            return $"embedded_{sourcePath.TrimStart('*')}_{type}";
        }
        return Path.GetFileNameWithoutExtension(sourcePath);
    }

    /// <summary>
    /// Process RMA textures: pack separate channels or use packed texture
    /// Returns path to cached RMA texture
    /// </summary>
    private static (string? path, string? hash) ProcessRMATexture(string? roughnessPath, string? metallicPath, string? aoPath, Assimp.Scene? scene, string sceneName, string meshName)
    {
        using var packedRMA = RMATexturePacker.PackFromPaths(roughnessPath, metallicPath, aoPath, scene);
        var hash = RMATexturePacker.ComputeRMAHash(packedRMA);

        if (AssetCache.HasTexture(hash, out string cachedPath))
        {
           // Console.WriteLine($"[CachedModelLoader] Found cached RMA: {cachedPath}");
            return (cachedPath, hash);
        }

        // Try to get a friendly name from one of the source components
        string friendlyName = "rma_packed";
        if (!string.IsNullOrEmpty(metallicPath)) friendlyName = Path.GetFileNameWithoutExtension(metallicPath) + "_rma";
        else if (!string.IsNullOrEmpty(roughnessPath)) friendlyName = Path.GetFileNameWithoutExtension(roughnessPath) + "_rma";
        
        cachedPath = AssetCache.RegisterTexture(hash, new TextureCacheMetadata
        {
            Width = packedRMA.Width,
            Height = packedRMA.Height,
            TextureType = TextureType.RMA
        }, sceneName, meshName, friendlyName);

        WlTexFormat.Write(cachedPath, packedRMA, TextureType.RMA, hash);
        Console.WriteLine($"[CachedModelLoader] Saved RMA: {cachedPath}");
        return (cachedPath, hash);
    }

    /// <summary>
    /// Load image from path (supports embedded glTF textures)
    /// </summary>
    private static Image<Rgba32>? LoadSourceImage(string? path, Assimp.Scene? scene)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // Embedded texture
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
}
