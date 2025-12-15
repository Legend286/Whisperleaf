using System.Numerics;
using System.Runtime.CompilerServices;

namespace Whisperleaf.Graphics.Scene;

public struct Frustum
{
    private Plane _p0, _p1, _p2, _p3, _p4, _p5;

    public Frustum(Matrix4x4 vp)
    {
        // Left
        _p0 = Plane.Normalize(new Plane(
            vp.M14 + vp.M11,
            vp.M24 + vp.M21,
            vp.M34 + vp.M31,
            vp.M44 + vp.M41));
        // Right
        _p1 = Plane.Normalize(new Plane(
            vp.M14 - vp.M11,
            vp.M24 - vp.M21,
            vp.M34 - vp.M31,
            vp.M44 - vp.M41));
        // Bottom
        _p2 = Plane.Normalize(new Plane(
            vp.M14 + vp.M12,
            vp.M24 + vp.M22,
            vp.M34 + vp.M32,
            vp.M44 + vp.M42));
        // Top
        _p3 = Plane.Normalize(new Plane(
            vp.M14 - vp.M12,
            vp.M24 - vp.M22,
            vp.M34 - vp.M32,
            vp.M44 - vp.M42));
        // Near
        _p4 = Plane.Normalize(new Plane(
            vp.M13,
            vp.M23,
            vp.M33,
            vp.M43));
        // Far
        _p5 = Plane.Normalize(new Plane(
            vp.M14 - vp.M13,
            vp.M24 - vp.M23,
            vp.M34 - vp.M33,
            vp.M44 - vp.M43));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Vector3 min, Vector3 max)
    {
        // Check AABB against all 6 planes
        // If AABB is completely "behind" any plane, it's culled.
        return IntersectsPlane(_p0, min, max) &&
               IntersectsPlane(_p1, min, max) &&
               IntersectsPlane(_p2, min, max) &&
               IntersectsPlane(_p3, min, max) &&
               IntersectsPlane(_p4, min, max) &&
               IntersectsPlane(_p5, min, max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IntersectsPlane(Plane p, Vector3 min, Vector3 max)
    {
        // Find the corner of the AABB most aligned with the plane normal
        float px = p.Normal.X >= 0 ? max.X : min.X;
        float py = p.Normal.Y >= 0 ? max.Y : min.Y;
        float pz = p.Normal.Z >= 0 ? max.Z : min.Z;

        // If that point is behind the plane, the whole box is behind
        return (p.Normal.X * px + p.Normal.Y * py + p.Normal.Z * pz + p.D) >= 0;
    }
}
