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
    public static (List<MeshData> meshes, List<MaterialData> materials, Assimp.Scene scene) Load(string path)
    {
        // Always load the source file with Assimp to get the scene structure
        var (sourceMeshes, sourceMaterials, scene) = AssimpLoader.LoadCPU(path);

        // Process meshes (check cache or process)
        var cachedMeshes = new List<MeshData>();
        foreach (var mesh in sourceMeshes)
        {
            var meshHash = WlMeshFormat.ComputeHash(mesh);

            if (AssetCache.HasMesh(meshHash, out string meshPath))
            {
                var cached = WlMeshFormat.Read(meshPath, out _);
                cached.Name = mesh.Name;
                cached.WorldMatrix = mesh.WorldMatrix;
                cachedMeshes.Add(cached);
            }
            else
            {
                meshPath = AssetCache.RegisterMesh(meshHash, new MeshCacheMetadata
                {
                    VertexCount = mesh.Vertices.Length / 12,
                    IndexCount = mesh.Indices.Length,
                    MaterialIndex = mesh.MaterialIndex
                });

                WlMeshFormat.Write(meshPath, mesh, meshHash);
                cachedMeshes.Add(mesh);
            }
        }

        // Process materials (check cache or process)
        var cachedMaterials = new List<MaterialData>();
        foreach (var mat in sourceMaterials)
        {
            mat.BaseColorPath = ProcessTexture(mat.BaseColorPath, TextureType.BaseColor, scene);
            mat.NormalPath = ProcessTexture(mat.NormalPath, TextureType.Normal, scene);
            mat.EmissivePath = ProcessTexture(mat.EmissivePath, TextureType.Emissive, scene);

            mat.MetallicPath = ProcessRMATexture(mat.RoughnessPath, mat.MetallicPath, mat.OcclusionPath, scene);
            mat.RoughnessTex = null;
            mat.OcclusionPath = null;
            mat.UsePackedRMA = true;

            cachedMaterials.Add(mat);
        }

        return (cachedMeshes, cachedMaterials, scene);
    }

    /// <summary>
    /// Process a single texture (check cache or compress and cache)
    /// </summary>
    private static string? ProcessTexture(string? sourcePath, TextureType textureType, Assimp.Scene? scene)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        Image<Rgba32>? sourceImage = LoadSourceImage(sourcePath, scene);
        if (sourceImage == null)
            return null;

        using (sourceImage)
        {
            var hash = WlTexFormat.ComputeHash(sourceImage);

            if (AssetCache.HasTexture(hash, out string cachedPath))
                return cachedPath;

            cachedPath = AssetCache.RegisterTexture(hash, new TextureCacheMetadata
            {
                Width = sourceImage.Width,
                Height = sourceImage.Height,
                TextureType = textureType
            });

            WlTexFormat.Write(cachedPath, sourceImage, textureType, hash);
            return cachedPath;
        }
    }

    /// <summary>
    /// Process RMA textures: pack separate channels or use packed texture
    /// Returns path to cached RMA texture
    /// </summary>
    private static string? ProcessRMATexture(string? roughnessPath, string? metallicPath, string? aoPath, Assimp.Scene? scene)
    {
        using var packedRMA = RMATexturePacker.PackFromPaths(roughnessPath, metallicPath, aoPath, scene);
        var hash = RMATexturePacker.ComputeRMAHash(packedRMA);

        if (AssetCache.HasTexture(hash, out string cachedPath))
            return cachedPath;

        cachedPath = AssetCache.RegisterTexture(hash, new TextureCacheMetadata
        {
            Width = packedRMA.Width,
            Height = packedRMA.Height,
            TextureType = TextureType.RMA
        });

        WlTexFormat.Write(cachedPath, packedRMA, TextureType.RMA, hash);
        return cachedPath;
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
