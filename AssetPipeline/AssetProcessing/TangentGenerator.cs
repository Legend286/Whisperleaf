// Whisperleaf/Graphics/Loaders/TangentGenerator.cs
using System.Numerics;

namespace Whisperleaf.AssetPipeline.AssetProcessing
{
    public static class TangentGenerator
    {
        // Generates tangent.xyz with handedness in .w
        public static Vector4[] Generate(Vector3[] pos, Vector3[] norm, Vector2[] uv, uint[] idx)
        {
            int vCount = pos.Length;
            var tan1 = new Vector3[vCount];
            var tan2 = new Vector3[vCount];
            var outT = new Vector4[vCount];

            for (int i = 0; i < idx.Length; i += 3)
            {
                uint i1 = idx[i], i2 = idx[i + 1], i3 = idx[i + 2];

                var p1 = pos[i1]; var p2 = pos[i2]; var p3 = pos[i3];
                var w1 = uv[i1];  var w2 = uv[i2];  var w3 = uv[i3];

                float x1 = p2.X - p1.X, x2 = p3.X - p1.X;
                float y1 = p2.Y - p1.Y, y2 = p3.Y - p1.Y;
                float z1 = p2.Z - p1.Z, z2 = p3.Z - p1.Z;

                float s1 = w2.X - w1.X, s2 = w3.X - w1.X;
                float t1 = w2.Y - w1.Y, t2 = w3.Y - w1.Y;

                float r = s1 * t2 - s2 * t1;
                float inv = MathF.Abs(r) < 1e-8f ? 1f : 1f / r;

                var sdir = new Vector3((t2 * x1 - t1 * x2) * inv,
                                       (t2 * y1 - t1 * y2) * inv,
                                       (t2 * z1 - t1 * z2) * inv);
                var tdir = new Vector3((s1 * x2 - s2 * x1) * inv,
                                       (s1 * y2 - s2 * y1) * inv,
                                       (s1 * z2 - s2 * z1) * inv);

                tan1[i1] += sdir; tan1[i2] += sdir; tan1[i3] += sdir;
                tan2[i1] += tdir; tan2[i2] += tdir; tan2[i3] += tdir;
            }

            for (int i = 0; i < vCount; i++)
            {
                var n = norm[i];
                var t = tan1[i];

                // Gram-Schmidt
                var tangent = Vector3.Normalize(t - n * Vector3.Dot(n, t));
                float w = (Vector3.Dot(Vector3.Cross(n, tangent), tan2[i]) < 0f) ? -1f : 1f;

                outT[i] = new Vector4(tangent, w);
            }
            return outT;
        }
    }
}
