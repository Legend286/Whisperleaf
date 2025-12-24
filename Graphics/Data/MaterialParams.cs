using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialParams
    {
        public Vector4 BaseColorFactor;   // 16 bytes, offset 0
        public Vector4 EmissiveFactor;    // 12 bytes, offset 16
        public float MetallicFactor;      // 4 bytes, offset 32
        public float RoughnessFactor;     // 4 bytes, offset 36
        public int UsePackedRMA;          // 4 bytes, offset 40
        public float AlphaCutoff;         // 4 bytes, offset 44
        public int AlphaMode;             // 4 bytes, offset 48
        private Vector3 _padding;         // 12 bytes, offset 52 (Total 64)

        public MaterialParams(Vector4 baseColorFactor, Vector3 emissiveFactor,
            float metallicFactor, float roughnessFactor, bool usePackedRMA, float alphaCutoff, int alphaMode)
        {
            BaseColorFactor = baseColorFactor;
            EmissiveFactor = new Vector4(emissiveFactor,1.0f);
            MetallicFactor = metallicFactor;
            RoughnessFactor = roughnessFactor;
            UsePackedRMA = usePackedRMA ? 1 : 0;
            AlphaCutoff = alphaCutoff;
            AlphaMode = alphaMode;
            _padding = Vector3.Zero;
        }
    }
}
