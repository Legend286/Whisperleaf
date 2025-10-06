using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Whisperleaf.Graphics.Scene.Data
{
    public class ModelUniformBuffer : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private readonly ResourceLayout _layout;
        private DeviceBuffer _buffer;
        private ResourceSet _resourceSet;
        private readonly uint _stride;
        private int _capacity;
        private readonly List<ModelUniform> _transforms = new();

        public ModelUniformBuffer(GraphicsDevice gd, int initialCapacity = 64)
        {
            _gd = gd;
            _stride = (uint)Marshal.SizeOf<ModelUniform>();
            _capacity = Math.Max(1, initialCapacity);

            var factory = gd.ResourceFactory;

            _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription(
                    "ModelTransforms",
                    ResourceKind.StructuredBufferReadOnly,
                    ShaderStages.Vertex)));

            _buffer = factory.CreateBuffer(new BufferDescription(
                (uint)_capacity * _stride,
                BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic,
                _stride));

            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));
        }

        public int Allocate(Matrix4x4 transform)
        {
            EnsureCapacity(_transforms.Count + 1);

            var uniformMatrix = Matrix4x4.Transpose(transform);
            var uniform = new ModelUniform(uniformMatrix);
            int index = _transforms.Count;
            _transforms.Add(uniform);
            _gd.UpdateBuffer(_buffer, (uint)(index * _stride), ref uniform);
            return index;
        }

        public void UpdateTransform(int index, Matrix4x4 transform)
        {
            if ((uint)index >= _transforms.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var uniformMatrix = Matrix4x4.Transpose(transform);
            var uniform = new ModelUniform(uniformMatrix);
            _transforms[index] = uniform;
            _gd.UpdateBuffer(_buffer, (uint)(index * _stride), ref uniform);
        }

        public void UpdateTransform(CommandList cl, int index, Matrix4x4 transform)
        {
            if ((uint)index >= _transforms.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var uniformMatrix = Matrix4x4.Transpose(transform);
            var uniform = new ModelUniform(uniformMatrix);
            _transforms[index] = uniform;
            cl.UpdateBuffer(_buffer, (uint)(index * _stride), ref uniform);
        }

        public void Clear()
        {
            _transforms.Clear();
        }

        public ResourceLayout Layout => _layout;
        public ResourceSet ResourceSet => _resourceSet;
        public int Count => _transforms.Count;

        public Matrix4x4 GetTransform(int index)
        {
            if ((uint)index >= _transforms.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return Matrix4x4.Transpose(_transforms[index].ModelMatrix);
        }

        private void EnsureCapacity(int desiredCount)
        {
            if (desiredCount <= _capacity)
                return;

            while (_capacity < desiredCount)
                _capacity *= 2;

            RecreateBuffer();
        }

        private void RecreateBuffer()
        {
            _buffer.Dispose();
            var factory = _gd.ResourceFactory;
            _buffer = factory.CreateBuffer(new BufferDescription(
                (uint)_capacity * _stride,
                BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic,
                _stride));

            _resourceSet.Dispose();
            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));

            for (int i = 0; i < _transforms.Count; i++)
            {
                var uniform = _transforms[i];
                _gd.UpdateBuffer(_buffer, (uint)(i * _stride), ref uniform);
            }
        }

        public void Dispose()
        {
            _buffer.Dispose();
            _layout.Dispose();
            _resourceSet.Dispose();
        }
    }
}
