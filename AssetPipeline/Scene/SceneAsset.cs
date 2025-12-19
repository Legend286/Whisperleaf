using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Whisperleaf.Utilities.Serialization;

namespace Whisperleaf.AssetPipeline.Scene;

/// <summary>
/// Represents a complete imported scene with hierarchy
/// Serialized to .wlscene format
/// </summary>
public class SceneAsset
{
    [JsonIgnore]
    public string ScenePath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    public SceneMetadata Metadata { get; set; } = new();
    public List<SceneNode> RootNodes { get; set; } = new();
    public List<MaterialReference> Materials { get; set; } = new();

    /// <summary>
    /// Save scene to .wlscene JSON file
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(path, json);
        ScenePath = Path.GetFullPath(path);
    }

    /// <summary>
    /// Load scene from .wlscene JSON file
    /// </summary>
    public static SceneAsset Load(string path)
    {
        var json = File.ReadAllText(path);
        var scene = JsonSerializer.Deserialize<SceneAsset>(json, SerializerOptions)
                    ?? throw new Exception($"Failed to deserialize scene: {path}");

        FixupTransforms(scene);
        scene.ScenePath = Path.GetFullPath(path);
        return scene;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new Matrix4x4JsonConverter());
        options.Converters.Add(new Vector3JsonConverter());
        options.Converters.Add(new Vector4JsonConverter());
        return options;
    }

    private static void FixupTransforms(SceneAsset scene)
    {
        foreach (var root in scene.RootNodes)
        {
            FixupNode(root);
        }
    }

    private static void FixupNode(SceneNode node)
    {
        if (IsZeroMatrix(node.LocalTransform))
        {
            node.LocalTransform = Matrix4x4.Identity;
        }

        foreach (var child in node.Children)
        {
            FixupNode(child);
        }
    }

    private static bool IsZeroMatrix(in Matrix4x4 matrix)
    {
        return matrix.M11 == 0f && matrix.M12 == 0f && matrix.M13 == 0f && matrix.M14 == 0f &&
               matrix.M21 == 0f && matrix.M22 == 0f && matrix.M23 == 0f && matrix.M24 == 0f &&
               matrix.M31 == 0f && matrix.M32 == 0f && matrix.M33 == 0f && matrix.M34 == 0f &&
               matrix.M41 == 0f && matrix.M42 == 0f && matrix.M43 == 0f && matrix.M44 == 0f;
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
    public int UpAxis { get; set; } = 1; // 0=X, 1=Y, 2=Z
    public int UpAxisSign { get; set; } = 1;
}

/// <summary>
/// Hierarchical scene node (mesh instance or transform node)
/// </summary>
public class SceneNode
{
    public string Name { get; set; } = string.Empty;
    public Matrix4x4 LocalTransform { get; set; } = Matrix4x4.Identity;
    public MeshReference? Mesh { get; set; }
    public SceneLight? Light { get; set; }
    public bool IsStatic { get; set; } = true;
    public bool IsVisible { get; set; } = true;
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

public class SceneLight
{
    public int Type { get; set; } // 0=Point, 1=Directional, 2=Spot
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    public float Range { get; set; } = 10.0f;
    public float InnerCone { get; set; } = 0.5f; // Radians
    public float OuterCone { get; set; } = 0.6f; // Radians
}
