using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.Graphics.Data;

namespace Whisperleaf.Graphics.Assets
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ModelPush
    {
        public Matrix4x4 Model;
    }

    public class MeshGpu : IDisposable
    {
        public MeshRange Range { get; }
        private readonly GeometryBuffer _geometryBuffer;
        
        public DeviceBuffer VertexBuffer => _geometryBuffer.VertexBuffer;
        public DeviceBuffer IndexBuffer => _geometryBuffer.IndexBuffer;
        
        public int IndexCount => (int)Range.IndexCount;
        public Matrix4x4 WorldMatrix { get; }
        public int MaterialIndex { get; }
        public Vector3 AABBMin { get; }
        public Vector3 AABBMax { get; }

        public MeshGpu(GeometryBuffer geometryBuffer, MeshData mesh)
        {
            _geometryBuffer = geometryBuffer;

            Range = geometryBuffer.Allocate(mesh.Vertices, mesh.Indices);

            WorldMatrix = mesh.WorldMatrix; 
            MaterialIndex = mesh.MaterialIndex;
            AABBMin = mesh.AABBMin;
            AABBMax = mesh.AABBMax;
        }

        public void Dispose()
        {
            _geometryBuffer.Free(Range);
        }
    }
}
