// Whisperleaf/Graphics/Assets/MeshGpu.cs
using Veldrid;
using Whisperleaf.AssetPipeline;

namespace Whisperleaf.Graphics.Assets
{
    public sealed class MeshGpu : IDisposable
    {
        public DeviceBuffer VertexBuffer { get; }
        public DeviceBuffer IndexBuffer { get; }
        public uint IndexCount { get; }

        public MeshGpu(GraphicsDevice gd, MeshData data)
        {
            var rf = gd.ResourceFactory;

            VertexBuffer = rf.CreateBuffer(new BufferDescription(
                (uint)(data.Vertices.Length * sizeof(float)), BufferUsage.VertexBuffer));
            gd.UpdateBuffer(VertexBuffer, 0, data.Vertices);

            IndexBuffer = rf.CreateBuffer(new BufferDescription(
                (uint)(data.Indices.Length * sizeof(uint)), BufferUsage.IndexBuffer));
            gd.UpdateBuffer(IndexBuffer, 0, data.Indices);

            IndexCount = (uint)data.Indices.Length;
        }

        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
        }
    }
}