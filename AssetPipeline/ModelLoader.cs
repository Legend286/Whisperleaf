// Whisperleaf/Graphics/Loaders/AssimpLoader.cs
using Assimp;
using System.Numerics;
using Whisperleaf.AssetPipeline;
using Veldrid.ImageSharp;
using Whisperleaf.AssetPipeline.AssetProcessing;

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
            PostProcessSteps.ImproveCacheLocality |
            PostProcessSteps.FlipUVs; // remove FlipUVs if you don't need it

        public static (List<MeshData> meshes, List<MaterialData> materials) LoadCPU(string path, bool decodeImages = true)
        {
            using var ctx = new AssimpContext();
            var scene = ctx.ImportFile(path, DefaultSteps)
                       ?? throw new Exception($"Assimp failed to load: {path}");

            // ----- Materials
            var materials = new List<MaterialData>(scene.MaterialCount);
            for (int i = 0; i < scene.MaterialCount; i++)
            {
                var aim = scene.Materials[i];
                var m = new MaterialData { Name = aim.Name ?? $"Material_{i}" };

                // Factors (Assimp isn't glTF-PBR native; map common properties)
                if (aim.HasColorDiffuse) m.BaseColorFactor = new Vector4(aim.ColorDiffuse.R, aim.ColorDiffuse.G, aim.ColorDiffuse.B, aim.HasOpacity ? aim.Opacity : 1f);
                if (aim.HasShininess) m.RoughnessFactor = 1f - Math.Clamp(aim.Shininess / 100f, 0f, 1f); // crude map
                if (aim.HasColorEmissive) m.EmissiveFactor = new Vector3(aim.ColorEmissive.R, aim.ColorEmissive.G, aim.ColorEmissive.B);

                // Texture paths (Assimp distinguishes by TextureType)
                m.BaseColorPath   = TryGetTex(aim, TextureType.BaseColor) ?? TryGetTex(aim, TextureType.Diffuse);
                m.NormalPath      = TryGetTex(aim, TextureType.NormalCamera) ?? TryGetTex(aim, TextureType.Normals);
                m.OcclusionPath   = TryGetTex(aim, TextureType.AmbientOcclusion) ?? TryGetTex(aim, TextureType.Ambient);
                m.EmissivePath    = TryGetTex(aim, TextureType.Emissive);
                m.MetallicPath    = TryGetTex(aim, TextureType.Metalness);
                m.RoughnessPath   = TryGetTex(aim, TextureType.Roughness);

                // (Optional) decode image files here; you can also defer until GPU upload
                if (decodeImages)
                {
                    m.BaseColorImage    = m.BaseColorPath   is null ? null : new ImageSharpTexture(m.BaseColorPath,   false);
                    m.NormalImage       = m.NormalPath      is null ? null : new ImageSharpTexture(m.NormalPath,      false);
                    m.OcclusionImage    = m.OcclusionPath   is null ? null : new ImageSharpTexture(m.OcclusionPath,   false);
                    m.EmissiveImage     = m.EmissivePath    is null ? null : new ImageSharpTexture(m.EmissivePath,    false);
                    m.MetallicImage     = m.MetallicPath    is null ? null : new ImageSharpTexture(m.MetallicPath,    false);
                    m.RoughnessImage    = m.RoughnessPath   is null ? null : new ImageSharpTexture(m.RoughnessPath,   false);
                }

                materials.Add(m);
            }

            // ----- Meshes (flattened from the node hierarchy)
            var meshes = new List<MeshData>(scene.MeshCount);
            for (int i = 0; i < scene.MeshCount; i++)
            {
                var am = scene.Meshes[i];

                var positions = new Vector3[am.VertexCount];
                var normals   = new Vector3[am.VertexCount];
                var uvs       = new Vector2[am.VertexCount];
                var tangents4 = new Vector4[am.VertexCount];

                for (int v = 0; v < am.VertexCount; v++)
                {
                    var p = am.Vertices[v];
                    positions[v] = new Vector3(p.X, p.Y, p.Z);

                    var n = am.Normals.Count > v ? am.Normals[v] : new Vector3D(0,1,0);
                    normals[v] = new Vector3(n.X, n.Y, n.Z);

                    if (am.TextureCoordinateChannelCount > 0 && am.TextureCoordinateChannels[0].Count > v)
                    {
                        var uv = am.TextureCoordinateChannels[0][v];
                        uvs[v] = new Vector2(uv.X, uv.Y);
                    }
                    else uvs[v] = Vector2.Zero;
                }

                // Indices (uint)
                var indices = am.GetUnsignedIndices();

                // Tangents:
                if (am.Tangents.Count == am.VertexCount)
                {
                    // Assimp provided T and (usually) BiTangents
                    bool hasBi = am.BiTangents.Count == am.VertexCount;
                    for (int v = 0; v < am.VertexCount; v++)
                    {
                        var t = am.Tangents[v];
                        var T = Vector3.Normalize(new Vector3(t.X, t.Y, t.Z));
                        float sign = 1f;

                        if (hasBi)
                        {
                            var b = am.BiTangents[v];
                            var B = new Vector3(b.X, b.Y, b.Z);
                            // sign from cross(N,T) vs B
                            sign = Vector3.Dot(Vector3.Cross(normals[v], T), B) < 0f ? -1f : 1f;
                        }

                        tangents4[v] = new Vector4(T, sign);
                    }
                }
                else
                {
                    tangents4 = TangentGenerator.Generate(positions, normals, uvs, indices);
                }

                // Interleave
                var interleaved = new float[am.VertexCount * 12];
                int w = 0;
                Vector3 aabbMin = new(float.MaxValue), aabbMax = new(float.MinValue);

                for (int v = 0; v < am.VertexCount; v++)
                {
                    var P = positions[v]; var N = normals[v];
                    var UV = uvs[v]; var T4 = tangents4[v];

                    aabbMin = Vector3.Min(aabbMin, P);
                    aabbMax = Vector3.Max(aabbMax, P);

                    interleaved[w++] = P.X;  interleaved[w++] = P.Y;  interleaved[w++] = P.Z;
                    interleaved[w++] = N.X;  interleaved[w++] = N.Y;  interleaved[w++] = N.Z;
                    interleaved[w++] = UV.X; interleaved[w++] = UV.Y;
                    interleaved[w++] = T4.X; interleaved[w++] = T4.Y; interleaved[w++] = T4.Z; interleaved[w++] = T4.W;
                }

                meshes.Add(new MeshData
                {
                    Name = am.Name,
                    Vertices = interleaved,
                    Indices = indices,
                    AABBMin = aabbMin,
                    AABBMax = aabbMax
                });
            }

            return (meshes, materials);
        }

        private static string? TryGetTex(Material m, TextureType type)
        {
            if (m.GetMaterialTextureCount(type) > 0 &&
                m.GetMaterialTexture(type, 0, out TextureSlot slot))
            {
                return slot.FilePath;
            }
            return null;
        }
    }
}
