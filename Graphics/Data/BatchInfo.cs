using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Data;

[StructLayout(LayoutKind.Sequential)]
public struct BatchInfo
{
    public uint InstanceStart;
    public uint InstanceCount;
    public uint VisibleOffset;
    public uint MeshIndex;
    public Vector4 AABBMin;
    public Vector4 AABBMax;

    public BatchInfo(uint instanceStart, uint instanceCount, uint visibleOffset, uint meshIndex, Vector3 min, Vector3 max)
    {
        InstanceStart = instanceStart;
        InstanceCount = instanceCount;
        VisibleOffset = visibleOffset;
        MeshIndex = meshIndex;
        AABBMin = new Vector4(min, 1.0f);
        AABBMax = new Vector4(max, 1.0f);
    }
}
