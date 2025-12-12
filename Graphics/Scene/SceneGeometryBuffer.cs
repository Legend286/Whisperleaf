using System;
using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.Scene;

public class SceneGeometryBuffer : IDisposable
{
    public DeviceBuffer VertexBuffer { get; private set; }
    public DeviceBuffer IndexBuffer { get; private set; }
    
    private readonly GraphicsDevice _gd;
    private uint _vertexCount = 0;
    private uint _indexCount = 0;
    
    private const uint VERTEX_STRIDE = 12 * 4; // 48 bytes
    private const uint INDEX_STRIDE = 4; // uint32

    public SceneGeometryBuffer(GraphicsDevice gd)
    {
        _gd = gd;
        // Start with some capacity
        EnsureVertexCapacity(10000);
        EnsureIndexCapacity(10000);
    }

    public MeshRange AddMesh(MeshData mesh)
    {
        uint vCount = (uint)mesh.Vertices.Length / 12;
        uint iCount = (uint)mesh.Indices.Length;

        EnsureVertexCapacity(_vertexCount + vCount);
        EnsureIndexCapacity(_indexCount + iCount);

        uint vOffset = _vertexCount;
        uint iOffset = _indexCount;

        _gd.UpdateBuffer(VertexBuffer, vOffset * VERTEX_STRIDE, mesh.Vertices);
        _gd.UpdateBuffer(IndexBuffer, iOffset * INDEX_STRIDE, mesh.Indices);

        _vertexCount += vCount;
        _indexCount += iCount;

        return new MeshRange(vOffset, iOffset, iCount, vCount, mesh.MaterialIndex, mesh.AABBMin, mesh.AABBMax);
    }

    public void Clear()
    {
        _vertexCount = 0;
        _indexCount = 0;
    }

    private void EnsureVertexCapacity(uint count)
    {
        uint currentSize = VertexBuffer?.SizeInBytes ?? 0;
        uint neededSize = count * VERTEX_STRIDE;

        if (neededSize > currentSize)
        {
            uint newSize = Math.Max(neededSize, currentSize * 2);
            newSize = Math.Max(newSize, 1024 * VERTEX_STRIDE); // Min size

            var newBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(newSize, BufferUsage.VertexBuffer));
            
            if (VertexBuffer != null)
            {
                if (_vertexCount > 0)
                {
                    using var cl = _gd.ResourceFactory.CreateCommandList();
                    cl.Begin();
                    cl.CopyBuffer(VertexBuffer, 0, newBuffer, 0, _vertexCount * VERTEX_STRIDE);
                    cl.End();
                    _gd.SubmitCommands(cl);
                    _gd.WaitForIdle();
                }
                VertexBuffer.Dispose();
            }
            
            VertexBuffer = newBuffer;
        }
    }

    private void EnsureIndexCapacity(uint count)
    {
        uint currentSize = IndexBuffer?.SizeInBytes ?? 0;
        uint neededSize = count * INDEX_STRIDE;

        if (neededSize > currentSize)
        {
            uint newSize = Math.Max(neededSize, currentSize * 2);
            newSize = Math.Max(newSize, 1024 * INDEX_STRIDE); // Min size

            var newBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(newSize, BufferUsage.IndexBuffer));
            
            if (IndexBuffer != null)
            {
                if (_indexCount > 0)
                {
                    using var cl = _gd.ResourceFactory.CreateCommandList();
                    cl.Begin();
                    cl.CopyBuffer(IndexBuffer, 0, newBuffer, 0, _indexCount * INDEX_STRIDE);
                    cl.End();
                    _gd.SubmitCommands(cl);
                    _gd.WaitForIdle();
                }
                IndexBuffer.Dispose();
            }
            
            IndexBuffer = newBuffer;
        }
    }

    public void Dispose()
    {
        VertexBuffer?.Dispose();
        IndexBuffer?.Dispose();
    }
}
