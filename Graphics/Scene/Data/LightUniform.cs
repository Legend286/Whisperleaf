using System.Collections.Specialized;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Scene.Data;

public enum LightType
{
    Point = 0,
    Directional = 1,
    Spot = 2
}

[StructLayout(LayoutKind.Sequential)]
public struct LightUniform
{
    public Vector4 Position;  // xyz = position, w = range
    public Vector4 Color;     // xyz = color, w = intensity
    public Vector4 Direction; // xyz = direction, w = type
    public Vector4 Params;    // x = innerCone, y = outerCone, z = shadowIndex, w = padding

    public LightUniform(Vector3 position, float range, Vector3 color, float intensity, LightType type = LightType.Point, Vector3 direction = default, float innerCone = 0, float outerCone = 0, int shadowIndex = -1) {
    
        Position = new Vector4(position, range);
        Color = new Vector4(color, intensity);
        Direction = new Vector4(direction, (float)type);
        Params = new Vector4(Single.DegreesToRadians(innerCone), Single.DegreesToRadians(outerCone), (float)shadowIndex, 0);
    }
}
