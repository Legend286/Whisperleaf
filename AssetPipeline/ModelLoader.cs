// Whisperleaf/Graphics/Loaders/AssimpLoader.cs
using Assimp;
using System.IO;
using System.Numerics;
using Assimp.Unmanaged;
using Whisperleaf.AssetPipeline;
using Veldrid.ImageSharp;
using Whisperleaf.AssetPipeline.AssetProcessing;
using Matrix4x4 = Assimp.Matrix4x4;

namespace Whisperleaf.Graphics.Loaders
{
    public static class AssimpLoader
    {
        // Import flags: triangulate, smooth normals, calc tangents; flip UVs if your engine expects that.
        private const PostProcessSteps DefaultSteps =
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.CalculateTangentSpace |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.ImproveCacheLocality;

        public static (List<MeshData> meshes, List<MaterialData> materials, Assimp.Scene scene) LoadCPU(string path)
        {
            using var ctx = new AssimpContext();
            var scene = ctx.ImportFile(path, DefaultSteps)
                        ?? throw new Exception($"Assimp failed to load: {path}");

            var modelDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();

            // ----- Materials
            var materials = new List<MaterialData>(scene.MaterialCount);
            for (int i = 0; i < scene.MaterialCount; i++)
            {
                var aim = scene.Materials[i];
                var m = new MaterialData { Name = aim.Name ?? $"Material_{i}" };

                // Factors (map Assimp’s properties to PBR-ish factors)
                if (aim.HasColorDiffuse)
                    m.BaseColorFactor = new Vector4(
                        aim.ColorDiffuse.R,
                        aim.ColorDiffuse.G,
                        aim.ColorDiffuse.B,
                        aim.HasOpacity ? aim.Opacity : 1f);

                if (aim.HasShininess)
                    m.RoughnessFactor = 1f - Math.Clamp(aim.Shininess / 100f, 0f, 1f); // crude shininess→roughness map

                if (aim.HasColorEmissive)
                    m.EmissiveFactor = new Vector3(
                        aim.ColorEmissive.R,
                        aim.ColorEmissive.G,
                        aim.ColorEmissive.B);

                // Debug logging for texture slots
                var activeSlots = new List<string>();
                foreach (TextureType type in Enum.GetValues(typeof(TextureType)))
                {
                    int count = aim.GetMaterialTextureCount(type);
                    if (count > 0)
                        activeSlots.Add($"{type}={count}");
                }
                Console.WriteLine($"[Assimp] Material '{m.Name}': {string.Join(" ", activeSlots)}");

                // Texture paths (Assimp distinguishes by TextureType, we normalize)
                m.BaseColorPath = ResolveTexture(scene, aim, TextureType.BaseColor, modelDir) ?? 
                                  ResolveTexture(scene, aim, TextureType.Diffuse, modelDir);
                
                m.NormalPath = ResolveTexture(scene, aim, TextureType.Normals, modelDir);
                m.EmissivePath = ResolveTexture(scene, aim, TextureType.Emissive, modelDir);

                // Try to load individual PBR maps first
                m.OcclusionPath = ResolveTexture(scene, aim, TextureType.AmbientOcclusion, modelDir) ??
                                  ResolveTexture(scene, aim, TextureType.Ambient, modelDir) ??
                                  ResolveTexture(scene, aim, TextureType.Lightmap, modelDir) ??
                                  ResolveTexture(scene, aim, TextureType.Unknown, modelDir);
                                  
                m.RoughnessPath = ResolveTexture(scene, aim, TextureType.Roughness, modelDir) ??
                                  ResolveTexture(scene, aim, TextureType.Unknown, modelDir);
                                  
                m.MetallicPath  = ResolveTexture(scene, aim, TextureType.Metalness, modelDir) ??
                                  ResolveTexture(scene, aim, TextureType.Unknown, modelDir);

                // Check for packed RMA texture
                // Case 1: All three point to the same texture
                // Case 2: Metallic and Roughness point to the same texture (common for glTF)
                if (!string.IsNullOrEmpty(m.MetallicPath) && m.MetallicPath == m.RoughnessPath)
                {
                    m.UsePackedRMA = true;
                    // If occlusion is null or points to the same texture, use that texture for all three
                    if (string.IsNullOrEmpty(m.OcclusionPath))
                    {
                        m.OcclusionPath = m.MetallicPath;
                    }
                }
                else
                {
                    m.UsePackedRMA = false;
                }

                materials.Add(m);

                // Auto-save .wlmat
                try 
                {
                    string safeName = string.Join("_", m.Name.Split(Path.GetInvalidFileNameChars()));
                    if (string.IsNullOrWhiteSpace(safeName)) safeName = "unnamed_material";
                    
                    string matPath = Path.Combine(modelDir, safeName + ".wlmat");
                    
                    var matAsset = new MaterialAsset {
                        Name = m.Name,
                        BaseColorFactor = m.BaseColorFactor,
                        EmissiveFactor = m.EmissiveFactor,
                        MetallicFactor = m.MetallicFactor,
                        RoughnessFactor = m.RoughnessFactor,
                        AlphaMode = AlphaMode.Opaque,
                        AlphaCutoff = 0.5f,
                        BaseColorTexture = m.BaseColorPath,
                        NormalTexture = m.NormalPath,
                        EmissiveTexture = m.EmissivePath,
                        RMATexture = m.UsePackedRMA ? m.MetallicPath : null 
                    };
                    
                    // Simple heuristic for alpha
                    if (aim.HasOpacity && aim.Opacity < 0.99f) matAsset.AlphaMode = AlphaMode.Blend;
                    if (m.Name.Contains("leaf", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("foliage", StringComparison.OrdinalIgnoreCase))
                    {
                        matAsset.AlphaMode = AlphaMode.Mask;
                    }

                    matAsset.Save(matPath);
                    Console.WriteLine($"[Assimp] Saved material asset: {matPath}");
                } 
                catch (Exception ex) 
                {
                    Console.WriteLine($"[Assimp] Failed to save material asset: {ex.Message}");
                }
            }

            // ----- Meshes (Direct 1:1 mapping)
            var meshes = new List<MeshData>(scene.MeshCount);
            for (int i = 0; i < scene.MeshCount; i++)
            {
                var am = scene.Meshes[i];
                var mesh = BuildMesh(am);
                mesh.WorldMatrix = System.Numerics.Matrix4x4.Identity; // Hierarchy handled by SceneImporter
                meshes.Add(mesh);
            }

            return (meshes, materials, scene);
        }
        
        // TraverseNode removed as we don't flatten anymore

        private static MeshData BuildMesh(Assimp.Mesh am)
        {
            var positions = new Vector3[am.VertexCount];
            var normals = new Vector3[am.VertexCount];
            var uvs = new Vector2[am.VertexCount];
            var tangents4 = new Vector4[am.VertexCount];
            
            bool hasUVs = am.TextureCoordinateChannelCount > 0 && am.TextureCoordinateChannels[0].Count == am.VertexCount;

            for (int v = 0; v < am.VertexCount; v++)
            {
                var p = am.Vertices[v];
                positions[v] = new Vector3(p.X, p.Y, p.Z);

                var n = am.Normals.Count > v ? am.Normals[v] : new Vector3D(0, 1, 0);
                normals[v] = new Vector3(n.X, n.Y, n.Z);

                if (hasUVs)
                {
                    var uv = am.TextureCoordinateChannels[0][v];
                    uvs[v] = new Vector2(uv.X, uv.Y);
                }
                else uvs[v] = Vector2.Zero;
            }

            var indices = am.GetUnsignedIndices();

            // Tangents
            if (am.Tangents.Count == am.VertexCount && am.BiTangents.Count == am.VertexCount)
            {
                for (int v = 0; v < am.VertexCount; v++)
                {
                    var t = am.Tangents[v];
                    var tVec = new Vector3(t.X, t.Y, t.Z);
                    var T = tVec.LengthSquared() > 1e-8f ? Vector3.Normalize(tVec) : new Vector3(1, 0, 0);

                    var b = am.BiTangents[v];
                    var B = new Vector3(b.X, b.Y, b.Z);
                    float sign = Vector3.Dot(Vector3.Cross(normals[v], T), B) < 0f ? -1f : 1f;

                    tangents4[v] = new Vector4(T, sign);
                }
            }
            else if (hasUVs)
            {
                tangents4 = TangentGenerator.Generate(positions, normals, uvs, indices);
            }
            else
            {
                // Fallback for missing tangents AND missing UVs
                // Use default tangent (1,0,0) and w=1
                var defaultTangent = new Vector4(1, 0, 0, 1);
                Array.Fill(tangents4, defaultTangent);
            }

            // Interleave
            var interleaved = new float[am.VertexCount * 12];
            int w = 0;
            Vector3 aabbMin = new(float.MaxValue), aabbMax = new(float.MinValue);

            for (int v = 0; v < am.VertexCount; v++)
            {
                var P = positions[v];
                var N = normals[v];
                var UV = uvs[v];
                var T4 = tangents4[v];

                aabbMin = Vector3.Min(aabbMin, P);
                aabbMax = Vector3.Max(aabbMax, P);

                interleaved[w++] = P.X;
                interleaved[w++] = P.Y;
                interleaved[w++] = P.Z;
                interleaved[w++] = N.X;
                interleaved[w++] = N.Y;
                interleaved[w++] = N.Z;
                interleaved[w++] = T4.X;
                interleaved[w++] = T4.Y;
                interleaved[w++] = T4.Z;
                interleaved[w++] = T4.W;
                interleaved[w++] = UV.X;
                interleaved[w++] = UV.Y;
            }

            return new MeshData
            {
                Name = am.Name,
                Vertices = interleaved,
                Indices = indices,
                AABBMin = aabbMin,
                AABBMax = aabbMax,
                WorldMatrix = System.Numerics.Matrix4x4.Identity, // default, filled by TraverseNode
                MaterialIndex = am.MaterialIndex
            };
        }

        // Resolves either external or embedded GLB textures
        private static string? ResolveTexture(Assimp.Scene scene, Material m, TextureType type, string modelDir)
        {
            if (m.GetMaterialTextureCount(type) > 0 &&
                m.GetMaterialTexture(type, 0, out TextureSlot slot))
            {
                if (!string.IsNullOrEmpty(slot.FilePath))
                {
                    if (slot.FilePath.StartsWith("*"))
                        return slot.FilePath; // Embedded texture marker understood by MaterialUploader

                    var texturePath = slot.FilePath;
                    try
                    {
                        texturePath = texturePath.Replace('\\', Path.DirectorySeparatorChar);
                        if (!Path.IsPathRooted(texturePath))
                            texturePath = Path.GetFullPath(Path.Combine(modelDir, texturePath));
                    }
                    catch
                    {
                        // If normalization fails, fall back to raw path; loader will attempt dummy texture
                    }

                    return texturePath;
                }
            }
            return null;
        }
    }
}
