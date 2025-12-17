namespace Whisperleaf.Maths;

public static class Interop
{
    public static System.Numerics.Matrix4x4 ToNumerics(this Assimp.Matrix4x4 m)
    {
        // Assimp is Row-Major (Translation in A4, B4, C4 - i.e. Column 4)
        // System.Numerics is Row-Major (Translation in Row 4)
        // We need to Transpose to move Column 4 to Row 4.
        return new System.Numerics.Matrix4x4(
            m.A1, m.B1, m.C1, m.D1,
            m.A2, m.B2, m.C2, m.D2,
            m.A3, m.B3, m.C3, m.D3,
            m.A4, m.B4, m.C4, m.D4
        );
    }

}