using System.Numerics;
using Veldrid.ImageSharp;

namespace Whisperleaf.AssetPipeline;

public sealed class MeshData
{
    public string Name;
    public float[] Vertices;
    public uint[] Indices;
    public Vector3 AABBMin;
    public Vector3 AABBMax;
    public Matrix4x4 WorldMatrix;
    public int MaterialIndex;
}
