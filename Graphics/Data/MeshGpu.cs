using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Whisperleaf.AssetPipeline;

namespace Whisperleaf.Graphics.Assets
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ModelPush
    {
        public Matrix4x4 Model;
    }

    public class MeshGpu : IDisposable
    {
        public DeviceBuffer VertexBuffer { get; }
        public DeviceBuffer IndexBuffer { get; }
        public int IndexCount { get; }
        public Matrix4x4 WorldMatrix { get; }
        public int MaterialIndex { get; }

        public MeshGpu(GraphicsDevice gd, MeshData mesh)
        {
            var factory = gd.ResourceFactory;

            VertexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(mesh.Vertices.Length * sizeof(float)), BufferUsage.VertexBuffer));
            gd.UpdateBuffer(VertexBuffer, 0, mesh.Vertices);

            IndexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(mesh.Indices.Length * sizeof(uint)), BufferUsage.IndexBuffer));
            gd.UpdateBuffer(IndexBuffer, 0, mesh.Indices);

            IndexCount = mesh.Indices.Length;
            WorldMatrix = mesh.WorldMatrix; // save Assimp world matrix
            MaterialIndex = mesh.MaterialIndex;
        }

        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
        }
    }
}
