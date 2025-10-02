using Assimp;

namespace Whisperleaf.Graphics.Data;

public class MeshCpu
{
    public float[] Vertices;
    public uint[] Indices;
    public Matrix4x4 WorldMatrix;

    public MeshCpu(float[] vertices, uint[] indices, Matrix4x4 worldMatrix)
    {
        Vertices = vertices;
        Indices = indices;
        WorldMatrix = worldMatrix;
    }
}
