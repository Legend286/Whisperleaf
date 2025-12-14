using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Whisperleaf.Graphics.Data
{
    public struct MeshRange
    {
        public int VertexOffset; // In vertices
        public uint IndexStart;  // In indices
        public uint IndexCount;
        public uint VertexCount;
    }

    public class GeometryBuffer : IDisposable
    {
        private const int InitialVertexCapacity = 100000; // ~4.8MB
        private const int InitialIndexCapacity = 300000;  // ~1.2MB
        private const int VertexSizeInBytes = 48; // 12 floats
        private const int IndexSizeInBytes = 4;   // uint32

        private readonly GraphicsDevice _gd;
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;

        private int _vertexCapacity;
        private int _indexCapacity;

        // Simple allocator: list of free blocks (Offset, Size)
        // Storing units as 'number of elements', not bytes
        private readonly List<(int Start, int Count)> _freeVertices = new();
        private readonly List<(int Start, int Count)> _freeIndices = new();
        
        // Track used ranges to help with debug or defrag if needed later
        // private readonly Dictionary<int, int> _usedVertices = new(); 

        public DeviceBuffer VertexBuffer => _vertexBuffer;
        public DeviceBuffer IndexBuffer => _indexBuffer;

        public GeometryBuffer(GraphicsDevice gd)
        {
            _gd = gd;
            _vertexCapacity = InitialVertexCapacity;
            _indexCapacity = InitialIndexCapacity;

            _vertexBuffer = CreateBuffer(gd, (uint)(_vertexCapacity * VertexSizeInBytes), BufferUsage.VertexBuffer);
            _indexBuffer = CreateBuffer(gd, (uint)(_indexCapacity * IndexSizeInBytes), BufferUsage.IndexBuffer);

            _freeVertices.Add((0, _vertexCapacity));
            _freeIndices.Add((0, _indexCapacity));
        }

        public MeshRange Allocate(float[] vertices, uint[] indices)
        {
            int vertexCount = vertices.Length / 12; // 12 floats per vertex
            int indexCount = indices.Length;

            // Allocate Vertices
            int vertexStart = AllocateBlock(_freeVertices, vertexCount, ref _vertexCapacity, ref _vertexBuffer, VertexSizeInBytes, BufferUsage.VertexBuffer);
            
            // Allocate Indices
            int indexStart = AllocateBlock(_freeIndices, indexCount, ref _indexCapacity, ref _indexBuffer, IndexSizeInBytes, BufferUsage.IndexBuffer);

            // Upload Data
            _gd.UpdateBuffer(_vertexBuffer, (uint)(vertexStart * VertexSizeInBytes), vertices);
            _gd.UpdateBuffer(_indexBuffer, (uint)(indexStart * IndexSizeInBytes), indices);

            return new MeshRange
            {
                VertexOffset = vertexStart,
                IndexStart = (uint)indexStart,
                IndexCount = (uint)indexCount,
                VertexCount = (uint)vertexCount
            };
        }

        public void Free(MeshRange range)
        {
            AddFreeBlock(_freeVertices, range.VertexOffset, (int)range.VertexCount);
            AddFreeBlock(_freeIndices, (int)range.IndexStart, (int)range.IndexCount);
        }

        private int AllocateBlock(List<(int Start, int Count)> freeList, int size, ref int capacity, ref DeviceBuffer buffer, int stride, BufferUsage usage)
        {
            // 1. Find best fit
            int bestIndex = -1;
            int bestSize = int.MaxValue;

            for (int i = 0; i < freeList.Count; i++)
            {
                if (freeList[i].Count >= size && freeList[i].Count < bestSize)
                {
                    bestSize = freeList[i].Count;
                    bestIndex = i;
                }
            }

            // 2. If no fit, Resize
            if (bestIndex == -1)
            {
                GrowBuffer(freeList, ref capacity, ref buffer, size, stride, usage);
                // Retry allocation (guaranteed to succeed now)
                return AllocateBlock(freeList, size, ref capacity, ref buffer, stride, usage);
            }

            // 3. Allocate
            var block = freeList[bestIndex];
            int start = block.Start;

            if (block.Count == size)
            {
                freeList.RemoveAt(bestIndex);
            }
            else
            {
                freeList[bestIndex] = (block.Start + size, block.Count - size);
            }

            return start;
        }

        private void GrowBuffer(List<(int Start, int Count)> freeList, ref int capacity, ref DeviceBuffer buffer, int neededSize, int stride, BufferUsage usage)
        {
            int oldCapacity = capacity;
            int newCapacity = Math.Max(oldCapacity * 2, oldCapacity + neededSize + 1024);
            
            // Create new buffer
            var newBuffer = CreateBuffer(_gd, (uint)(newCapacity * stride), usage);

            // Copy old data
            using (var cl = _gd.ResourceFactory.CreateCommandList())
            {
                cl.Begin();
                cl.CopyBuffer(buffer, 0, newBuffer, 0, (uint)(oldCapacity * stride));
                cl.End();
                _gd.SubmitCommands(cl);
                _gd.WaitForIdle();
            }

            // Dispose old
            buffer.Dispose();
            buffer = newBuffer;
            
            // Add new range to free list
            int addedSize = newCapacity - oldCapacity;
            AddFreeBlock(freeList, oldCapacity, addedSize);

            capacity = newCapacity;
        }

        private static DeviceBuffer CreateBuffer(GraphicsDevice gd, uint size, BufferUsage usage)
        {
            return gd.ResourceFactory.CreateBuffer(new BufferDescription(size, usage));
        }

        private void AddFreeBlock(List<(int Start, int Count)> freeList, int start, int count)
        {
            // Simplified: just add. Defragmentation is better but out of scope for MVP.
            freeList.Add((start, count));
        }

        public void Dispose()
        {
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
        }
    }
}
