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
        private int _padding2;            // 4 bytes, offset 44 (changed to int to match GLSL)

        public MaterialParams(Vector4 baseColorFactor, Vector3 emissiveFactor,
            float metallicFactor, float roughnessFactor, bool usePackedRMA)
        {
            BaseColorFactor = baseColorFactor;
            EmissiveFactor = new Vector4(emissiveFactor,1.0f);
            MetallicFactor = metallicFactor;
            RoughnessFactor = roughnessFactor;
            UsePackedRMA = usePackedRMA ? 1 : 0;
            _padding2 = 0;
        }
    }
}
