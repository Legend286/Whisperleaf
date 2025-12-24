using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Scene.Data;

[StructLayout(LayoutKind.Sequential)]
public struct CsmUniform
{
    public Matrix4x4 CascadeViewProj0;
    public Matrix4x4 CascadeViewProj1;
    public Matrix4x4 CascadeViewProj2;
    public Matrix4x4 CascadeViewProj3;
    public Vector4 CascadeSplits; // x, y, z, w
}
