using System.Numerics;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.Graphics.Loaders;

namespace Whisperleaf.AssetPipeline.Scene;

/// <summary>
/// Import glTF/GLB files into .wlscene format
/// Processes all assets through cache and creates scene hierarchy
/// </summary>
public static class SceneImporter
{
    /// <summary>
    /// Import glTF file and create .wlscene asset
    /// </summary>
    /// <param name="gltfPath">Path to glTF/GLB file</param>
    /// <param name="outputPath">Path to save .wlscene file (optional, defaults to same dir)</param>
    /// <param name="scaleFactor">Scale factor to apply to entire scene</param>
    /// <returns>Imported scene asset</returns>
    public static SceneAsset Import(string gltfPath, string? outputPath = null, float scaleFactor = 1.0f)
    {
        // Load and process through cache
        var (meshes, materials, assimpScene) = CachedModelLoader.Load(gltfPath);

        // Create scene asset
        var scene = new SceneAsset
        {
            Name = Path.GetFileNameWithoutExtension(gltfPath),
            SourceFile = Path.GetFullPath(gltfPath),
            ImportDate = DateTime.Now
        };

        // Compute scene bounds
        Vector3 boundsMin = new Vector3(float.MaxValue);
        Vector3 boundsMax = new Vector3(float.MinValue);
        int totalVertexCount = 0;
        int totalTriCount = 0;

        foreach (var mesh in meshes)
        {
            boundsMin = Vector3.Min(boundsMin, mesh.AABBMin);
            boundsMax = Vector3.Max(boundsMax, mesh.AABBMax);
            totalVertexCount += mesh.Vertices.Length / 12;
            totalTriCount += mesh.Indices.Length / 3;
        }

        scene.Metadata = new SceneMetadata
        {
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
            ScaleFactor = scaleFactor,
            TotalMeshCount = meshes.Count,
            TotalVertexCount = totalVertexCount,
            TotalTriangleCount = totalTriCount
        };

        // Build material references
        foreach (var mat in materials)
        {
            scene.Materials.Add(new MaterialReference
            {
                Name = mat.Name,
                BaseColorFactor = mat.BaseColorFactor,
                EmissiveFactor = mat.EmissiveFactor,
                MetallicFactor = mat.MetallicFactor,
                RoughnessFactor = mat.RoughnessFactor,
                BaseColorHash = GetTextureHash(mat.BaseColorPath),
                NormalHash = GetTextureHash(mat.NormalPath),
                RMAHash = GetTextureHash(mat.MetallicPath),
                EmissiveHash = GetTextureHash(mat.EmissivePath)
            });
        }

        // Build scene hierarchy from Assimp scene
        scene.RootNodes = BuildHierarchy(assimpScene.RootNode, meshes, Matrix4x4.CreateScale(scaleFactor));

        // Save scene file
        outputPath ??= Path.ChangeExtension(gltfPath, ".wlscene");
        scene.Save(outputPath);

        return scene;
    }

    /// <summary>
    /// Build scene hierarchy from Assimp node tree
    /// </summary>
    private static List<SceneNode> BuildHierarchy(Assimp.Node assimpNode, List<MeshData> meshes, Matrix4x4 scaleTransform, Matrix4x4? parentTransform = null)
    {
        var nodes = new List<SceneNode>();

        // Process this node
        var localTransform = Maths.Interop.ToNumerics(assimpNode.Transform);
        if (parentTransform == null)
        {
            localTransform = localTransform * scaleTransform;
        }

        // If node has meshes, create a scene node for each
        if (assimpNode.MeshIndices.Count > 0)
        {
            foreach (int meshIndex in assimpNode.MeshIndices)
            {
                var mesh = meshes[meshIndex];
                var meshHash = WlMeshFormat.ComputeHash(mesh);

                var node = new SceneNode
                {
                    Name = string.IsNullOrEmpty(mesh.Name) ? $"Mesh_{meshIndex}" : mesh.Name,
                    LocalTransform = Matrix4x4.CreateTranslation(mesh.CenteringOffset) * localTransform,
                    Mesh = new MeshReference
                    {
                        MeshHash = meshHash,
                        MaterialIndex = mesh.MaterialIndex,
                        AABBMin = mesh.AABBMin,
                        AABBMax = mesh.AABBMax,
                        VertexCount = mesh.Vertices.Length / 12,
                        IndexCount = mesh.Indices.Length
                    }
                };

                nodes.Add(node);
            }
        }
        else if (assimpNode.Children.Count > 0)
        {
            // Transform-only node
            var node = new SceneNode
            {
                Name = assimpNode.Name,
                LocalTransform = localTransform
            };

            nodes.Add(node);
        }

        // Process children
        foreach (var child in assimpNode.Children)
        {
            var childNodes = BuildHierarchy(child, meshes, scaleTransform, localTransform);

            if (nodes.Count > 0)
            {
                nodes[0].Children.AddRange(childNodes);
            }
            else
            {
                nodes.AddRange(childNodes);
            }
        }

        return nodes;
    }

    /// <summary>
    /// Extract texture hash from cache path
    /// </summary>
    private static string? GetTextureHash(string? cachePath)
    {
        if (string.IsNullOrEmpty(cachePath))
            return null;

        // Cache path format: ~/.cache/whisperleaf/textures/{hash}.wltex
        var fileName = Path.GetFileNameWithoutExtension(cachePath);
        return fileName;
    }

    /// <summary>
    /// Get import preview info without saving
    /// </summary>
    public static SceneMetadata GetImportPreview(string gltfPath)
    {
        var (meshes, _, _) = CachedModelLoader.Load(gltfPath);

        Vector3 boundsMin = new Vector3(float.MaxValue);
        Vector3 boundsMax = new Vector3(float.MinValue);
        int totalVertexCount = 0;
        int totalTriCount = 0;

        foreach (var mesh in meshes)
        {
            boundsMin = Vector3.Min(boundsMin, mesh.AABBMin);
            boundsMax = Vector3.Max(boundsMax, mesh.AABBMax);
            totalVertexCount += mesh.Vertices.Length / 12;
            totalTriCount += mesh.Indices.Length / 3;
        }

        return new SceneMetadata
        {
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
            ScaleFactor = 1.0f,
            TotalMeshCount = meshes.Count,
            TotalVertexCount = totalVertexCount,
            TotalTriangleCount = totalTriCount
        };
    }
}
