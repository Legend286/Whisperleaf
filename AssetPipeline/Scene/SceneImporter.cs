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
        string sceneName = Path.GetFileNameWithoutExtension(gltfPath);
        var (meshes, materials, assimpScene) = CachedModelLoader.Load(gltfPath, sceneName);

        // Create scene asset
        var scene = new SceneAsset
        {
            Name = sceneName,
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

        // Detect Up Axis from metadata
        if (assimpScene.Metadata != null)
        {
            if (assimpScene.Metadata.TryGetValue("UpAxis", out var upAxisEntry) && upAxisEntry.DataType == Assimp.MetaDataType.Int32)
                scene.Metadata.UpAxis = upAxisEntry.DataAs<int>() ?? 1;

            if (assimpScene.Metadata.TryGetValue("UpAxisSign", out var upAxisSignEntry) && upAxisSignEntry.DataType == Assimp.MetaDataType.Int32)
                scene.Metadata.UpAxisSign = upAxisSignEntry.DataAs<int>() ?? 1;
        }

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
                BaseColorHash = mat.BaseColorHash,
                NormalHash = mat.NormalHash,
                RMAHash = mat.RMAHash,
                EmissiveHash = mat.EmissiveHash
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
        // Transform is corrected by Interop.ToNumerics (Transposed)
        var localTransform = Maths.Interop.ToNumerics(assimpNode.Transform);
        if (parentTransform == null)
        {
            localTransform = localTransform * scaleTransform;
        }

        // Create container node representing this Assimp node
        var containerNode = new SceneNode
        {
            Name = assimpNode.Name,
            LocalTransform = localTransform
        };
        nodes.Add(containerNode);

        // If node has meshes, create child nodes for them
        // This preserves the structure (Node -> [Mesh1, Mesh2, Child1...])
        if (assimpNode.MeshIndices.Count > 0)
        {
            foreach (int meshIndex in assimpNode.MeshIndices)
            {
                var mesh = meshes[meshIndex];
                var meshHash = WlMeshFormat.ComputeHash(mesh);

                var meshNode = new SceneNode
                {
                    Name = string.IsNullOrEmpty(mesh.Name) ? $"Mesh_{meshIndex}" : mesh.Name,
                    LocalTransform = Matrix4x4.CreateTranslation(mesh.CenteringOffset),
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

                containerNode.Children.Add(meshNode);
            }
        }

        // Process children
        foreach (var child in assimpNode.Children)
        {
            var childNodes = BuildHierarchy(child, meshes, scaleTransform, localTransform);
            containerNode.Children.AddRange(childNodes);
        }

        return nodes;
    }

    /// <summary>
    /// Get import preview info without saving
    /// </summary>
    public static SceneMetadata GetImportPreview(string gltfPath)
    {
        string sceneName = Path.GetFileNameWithoutExtension(gltfPath);
        var (meshes, _, _) = CachedModelLoader.Load(gltfPath, sceneName);

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
