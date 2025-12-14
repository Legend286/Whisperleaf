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
        
        // These are removed as they are now in GeometryBuffer
        // public DeviceBuffer VertexBuffer { get; }
        // public DeviceBuffer IndexBuffer { get; }
        
        public int IndexCount => (int)Range.IndexCount;
        public Matrix4x4 WorldMatrix { get; }
        public int MaterialIndex { get; }

        public MeshGpu(GeometryBuffer geometryBuffer, MeshData mesh)
        {
            _geometryBuffer = geometryBuffer;

            Range = geometryBuffer.Allocate(mesh.Vertices, mesh.Indices);

            WorldMatrix = mesh.WorldMatrix; 
            MaterialIndex = mesh.MaterialIndex;
        }

        public void Dispose()
        {
            _geometryBuffer.Free(Range);
        }
    }
}
