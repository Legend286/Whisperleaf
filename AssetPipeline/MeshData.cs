using System.Numerics;
using Veldrid.ImageSharp;

namespace Whisperleaf.AssetPipeline;

public sealed class MeshData
{
    // Interleaved: pos(3), norm(3), uv(2), tangent(4)
    public float[] Vertices = Array.Empty<float>();
    public uint[] Indices = Array.Empty<uint>();
    public string? Name;
    // Optional bounds if you want them later
    public Vector3 AABBMin, AABBMax;
}