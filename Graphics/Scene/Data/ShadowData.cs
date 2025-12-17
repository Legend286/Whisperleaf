using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Scene.Data;

[StructLayout(LayoutKind.Sequential)]
public struct ShadowData
{
    public Matrix4x4 ViewProj;
    public Vector4 AtlasRect; // x, y (offset in UV), z (scale), w (layer)

    public ShadowData(Matrix4x4 viewProj, Vector4 atlasRect)
    {
        ViewProj = viewProj;
        AtlasRect = atlasRect;
    }
}
