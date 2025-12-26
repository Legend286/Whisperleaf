using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Whisperleaf.Utilities.Serialization;

namespace Whisperleaf.AssetPipeline
{
    public enum AlphaMode
    {
        Opaque = 0,
        Mask = 1,
        Blend = 2
    }

    public class MaterialAsset
    {
        public string Name { get; set; } = "New Material";
        
        [JsonIgnore]
        public string FilePath { get; set; } = string.Empty;

        // PBR Factors
        public Vector4 BaseColorFactor { get; set; } = Vector4.One;
        public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;
        public float MetallicFactor { get; set; } = 0.0f;
        public float RoughnessFactor { get; set; } = 1.0f;
        
        // Alpha
        public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;
        public float AlphaCutoff { get; set; } = 0.5f;

        // Texture Asset Paths (Relative to project root or asset cache?)
        // We will store the GUID or Hash of the texture ideally, but for now paths are easier to debug.
        // The editor currently deals with .wltex files in Cache/Textures.
        
        public string? BaseColorTexture { get; set; }
        public string? NormalTexture { get; set; }
        public string? RMATexture { get; set; } // Packed Roughness/Metallic/AO
        public string? EmissiveTexture { get; set; }
        
        // Hashes for runtime lookup (if we want to keep that pattern)
        // But if we point to a file, we can just load that file.
        // Let's stick to paths for the Editor workflow. 
        // When drag-dropping from AssetBrowser, we get a path.
        
        private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

        public void Save(string path)
        {
            var json = JsonSerializer.Serialize(this, SerializerOptions);
            File.WriteAllText(path, json);
            FilePath = Path.GetFullPath(path);
        }

        public static MaterialAsset Load(string path)
        {
            var json = File.ReadAllText(path);
            var asset = JsonSerializer.Deserialize<MaterialAsset>(json, SerializerOptions) 
                        ?? throw new Exception($"Failed to deserialize material: {path}");
            asset.FilePath = Path.GetFullPath(path);
            return asset;
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
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
