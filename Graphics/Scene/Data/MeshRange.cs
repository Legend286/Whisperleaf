using System.Numerics;
using Veldrid;

namespace Whisperleaf.Graphics.Scene.Data;

public readonly struct MeshRange
{
    public readonly uint VertexOffset;
    public readonly uint IndexOffset;
    public readonly uint IndexCount;
    public readonly uint VertexCount;
    public readonly int MaterialIndex;
    public readonly Vector3 AABBMin;
    public readonly Vector3 AABBMax;

    public MeshRange(uint vertexOffset, uint indexOffset, uint indexCount, uint vertexCount, int materialIndex, Vector3 aabbMin, Vector3 aabbMax)
    {
        VertexOffset = vertexOffset;
        IndexOffset = indexOffset;
        IndexCount = indexCount;
        VertexCount = vertexCount;
        MaterialIndex = materialIndex;
        AABBMin = aabbMin;
        AABBMax = aabbMax;
    }
}
