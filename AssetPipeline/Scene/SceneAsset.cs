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
        return options;
    }

    private static void FixupTransforms(SceneAsset scene)
    {
        foreach (var root in scene.RootNodes)
        {
            FixupNode(root);
        }
    }

    private static void FixupNode(SceneNode node, SceneNode? parent = null)
    {
        node.Parent = parent;
        if (IsZeroMatrix(node.LocalTransform))
        {
            node.LocalTransform = Matrix4x4.Identity;
        }

        foreach (var child in node.Children)
        {
            FixupNode(child, node);
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
}

/// <summary>
/// Hierarchical scene node (mesh instance or transform node)
/// </summary>
public class SceneNode
{
    private Matrix4x4 _localTransform = Matrix4x4.Identity;

    public string Name { get; set; } = string.Empty;
    
    public Matrix4x4 LocalTransform
    {
        get => _localTransform;
        set
        {
            _localTransform = value;
            IsDirty = true;
        }
    }

    public MeshReference? Mesh { get; set; }
    public List<SceneNode> Children { get; set; } = new();

    [JsonIgnore] public bool IsDirty { get; set; } = true;
    [JsonIgnore] public Matrix4x4 WorldMatrix { get; set; } = Matrix4x4.Identity;
    [JsonIgnore] public SceneNode? Parent { get; set; }
    [JsonIgnore] public Vector3 WorldBoundMin { get; set; }
    [JsonIgnore] public Vector3 WorldBoundMax { get; set; }

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

    public void UpdateWorld(Matrix4x4 parentWorld)
    {
        WorldMatrix = LocalTransform * parentWorld;
        
        if (Mesh != null)
        {
            CalculateWorldAABB();
        }

        IsDirty = false;

        foreach (var child in Children)
        {
            child.UpdateWorld(WorldMatrix);
        }
    }

    private void CalculateWorldAABB()
    {
        if (Mesh == null) return;

        var min = Mesh.AABBMin;
        var max = Mesh.AABBMax;

        var corners = new Vector3[8];
        corners[0] = new Vector3(min.X, min.Y, min.Z);
        corners[1] = new Vector3(max.X, min.Y, min.Z);
        corners[2] = new Vector3(min.X, max.Y, min.Z);
        corners[3] = new Vector3(max.X, max.Y, min.Z);
        corners[4] = new Vector3(min.X, min.Y, max.Z);
        corners[5] = new Vector3(max.X, min.Y, max.Z);
        corners[6] = new Vector3(min.X, max.Y, max.Z);
        corners[7] = new Vector3(max.X, max.Y, max.Z);

        var newMin = new Vector3(float.MaxValue);
        var newMax = new Vector3(float.MinValue);

        for (int i = 0; i < 8; i++)
        {
            var transformed = Vector3.Transform(corners[i], WorldMatrix);
            newMin = Vector3.Min(newMin, transformed);
            newMax = Vector3.Max(newMax, transformed);
        }

        WorldBoundMin = newMin;
        WorldBoundMax = newMax;
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
