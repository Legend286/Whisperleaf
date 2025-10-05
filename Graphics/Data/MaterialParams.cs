using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialParams
    {
        public Vector4 BaseColorFactor;   // 16 bytes
        public Vector3 EmissiveFactor;    // 12 bytes
        private float _padding1;          // 4 bytes
        public float MetallicFactor;      // 4 bytes (completes 16-byte block)
        public float RoughnessFactor;     // 4 bytes
        public int UsePackedRMA;          // 4 bytes
        private float _padding2;          // 4 bytes (completes 16-byte block)

        public MaterialParams(Vector4 baseColorFactor, Vector3 emissiveFactor,
            float metallicFactor, float roughnessFactor, bool usePackedRMA)
        {
            BaseColorFactor = baseColorFactor;
            EmissiveFactor = emissiveFactor;
            MetallicFactor = metallicFactor;
            RoughnessFactor = roughnessFactor;
            UsePackedRMA = usePackedRMA ? 1 : 0;
            _padding1 = 0;
            _padding2 = 0;
        }
    }
}
