using System.Numerics;
using System.Text.Json;

namespace Whisperleaf.AssetPipeline.Scene;

/// <summary>
/// Represents a complete imported scene with hierarchy
/// Serialized to .wlscene format
/// </summary>
public class SceneAsset
{
    public string Name { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    public SceneMetadata Metadata { get; set; } = new();
    public List<SceneNode> RootNodes { get; set; } = new();
    public List<MaterialReference> Materials { get; set; } = new();

    /// <summary>
    /// Save scene to .wlscene JSON file
    /// </summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = false
        });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Load scene from .wlscene JSON file
    /// </summary>
    public static SceneAsset Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SceneAsset>(json)
               ?? throw new Exception($"Failed to deserialize scene: {path}");
    }
}

/// <summary>
/// Scene metadata from import
/// </summary>
public class SceneMetadata
{
    public Vector3 BoundsMin { get; set; }
    public Vector3 BoundsMax { get; set; }
    public Vector3 BoundsCenter => (BoundsMin + BoundsMax) * 0.5f;
    public Vector3 BoundsSize => BoundsMax - BoundsMin;
    public float ScaleFactor { get; set; } = 1.0f;
    public int TotalMeshCount { get; set; }
    public int TotalVertexCount { get; set; }
    public int TotalTriangleCount { get; set; }
}

/// <summary>
/// Hierarchical scene node (mesh instance or transform node)
/// </summary>
public class SceneNode
{
    public string Name { get; set; } = string.Empty;
    public Matrix4x4 LocalTransform { get; set; } = Matrix4x4.Identity;
    public MeshReference? Mesh { get; set; }
    public List<SceneNode> Children { get; set; } = new();

    /// <summary>
    /// Get all mesh nodes in this subtree (for rendering)
    /// </summary>
    public IEnumerable<SceneNode> GetMeshNodes()
    {
        if (Mesh != null)
            yield return this;

        foreach (var child in Children)
        {
            foreach (var meshNode in child.GetMeshNodes())
                yield return meshNode;
        }
    }

    /// <summary>
    /// Compute world transform from root
    /// </summary>
    public Matrix4x4 ComputeWorldTransform(Matrix4x4 parentTransform)
    {
        return LocalTransform * parentTransform;
    }
}

/// <summary>
/// Reference to a cached mesh asset
/// </summary>
public class MeshReference
{
    public string MeshHash { get; set; } = string.Empty;
    public int MaterialIndex { get; set; }
    public Vector3 AABBMin { get; set; }
    public Vector3 AABBMax { get; set; }
    public int VertexCount { get; set; }
    public int IndexCount { get; set; }
}

/// <summary>
/// Reference to material with all texture paths
/// </summary>
public class MaterialReference
{
    public string Name { get; set; } = string.Empty;
    public Vector4 BaseColorFactor { get; set; } = Vector4.One;
    public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;
    public float MetallicFactor { get; set; } = 1.0f;
    public float RoughnessFactor { get; set; } = 1.0f;

    // Cached texture hashes (point to .wltex files in cache)
    public string? BaseColorHash { get; set; }
    public string? NormalHash { get; set; }
    public string? RMAHash { get; set; }
    public string? EmissiveHash { get; set; }
}
