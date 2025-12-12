using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid; // For IndirectDrawIndexedArguments

namespace Whisperleaf.Graphics.Data
{
    // Matches Veldrid.IndirectDrawIndexedArguments (but using uint for VertexOffset)
    [StructLayout(LayoutKind.Sequential)]
    public struct IndirectDrawIndexedArguments
    {
        public uint IndexCount;
        public uint InstanceCount;
        public uint FirstIndex;
        public int VertexOffset; // int to allow negative if needed, but usually positive.
        public uint FirstInstance;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MeshInfoGPU
    {
        public uint VertexOffset;
        public uint IndexOffset;
        public uint IndexCount;
        public int MaterialIndex; // Used by shader to fetch material data (textures etc)

        // Local AABB in mesh space
        public Vector3 AABBMin;
        private uint _padding1; // Pad to 16 bytes (Vector3 is 12 bytes)
        public Vector3 AABBMax;
        private uint _padding2; // Pad to 16 bytes

        // Size: 4*4 + 4*4 + 4*4 = 16+16+16 = 48 bytes. (Corrected: 4 (uints) * 4 bytes + 1 (int) * 4 bytes + 2 (Vector3) * 12 bytes = 16 + 4 + 24 = 44 bytes. Pad to 48 for simplicity)
        // If the Vector3 was 16 bytes aligned due to float4, then 16+16+16 = 48.
        // Let's ensure explicit padding.

        // In C#, Vector3 is 12 bytes.
        // We will make it 64 bytes total for simplicity to ensure alignment.
        // uint VertexOffset;      // 0-3
        // uint IndexOffset;       // 4-7
        // uint IndexCount;        // 8-11
        // int MaterialIndex;      // 12-15
        // Vector3 AABBMin;        // 16-27 (needs to be 16-aligned)
        // float _padding1;        // 28-31
        // Vector3 AABBMax;        // 32-43 (needs to be 16-aligned)
        // float _padding2;        // 44-47
        // float _padding3, _padding4, _padding5, _padding6; // 48-63

        // Let's use simpler padding that ensures 16-byte alignment where needed for members.
        // Veldrid StructLayoutKind.Sequential is usually good.
        // If a struct is passed to GPU, it will be treated as std140 or std430.
        // float3 is treated as float4 for alignment in std140.
        // So Vector3 (12) will occupy 16 bytes.
        // So, 4 uints (16) + int (4) + Vec3 (16) + Vec3 (16) = 16+4+16+16 = 52.
        // Pad to 64 bytes for safety.
        private uint _padding3; // 4 bytes
        private uint _padding4; // 4 bytes
        private uint _padding5; // 4 bytes
        private uint _padding6; // 4 bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceDataGPU
    {
        public Matrix4x4 WorldMatrix; // 64 bytes
        public uint MeshInfoIndex;    // 0-3
        private uint _padding1;       // 4-7
        private uint _padding2;       // 8-11
        private uint _padding3;       // 12-15 (So MeshInfoIndex + 3 padding is 16 bytes)
        // Total size: 64 + 16 = 80 bytes.
    }
}
