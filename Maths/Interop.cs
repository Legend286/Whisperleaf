namespace Whisperleaf.Maths;

public static class Interop
{
    public static System.Numerics.Matrix4x4 ToNumerics(this Assimp.Matrix4x4 m)
    {
        return new System.Numerics.Matrix4x4(
            m.A1, m.A2, m.A3, m.A4,
            m.B1, m.B2, m.B3, m.B4,
            m.C1, m.C2, m.C3, m.C4,
            m.D1, m.D2, m.D3, m.D4
        );
    }

}