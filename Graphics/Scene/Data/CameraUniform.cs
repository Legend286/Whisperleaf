using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Scene.Data;

[StructLayout(LayoutKind.Sequential)]
public struct CameraUniform
{
    public Matrix4x4 ViewProjection;
    
    public CameraUniform(Matrix4x4 viewProjection)
    {
        ViewProjection = viewProjection;
    }
}