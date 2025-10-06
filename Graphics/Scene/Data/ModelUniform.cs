using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Scene.Data;

[StructLayout(LayoutKind.Sequential)]
public struct ModelUniform
{
    public Matrix4x4 ModelMatrix;

    public ModelUniform(Matrix4x4 modelMatrix)
    {
        ModelMatrix = modelMatrix;
    }
}
