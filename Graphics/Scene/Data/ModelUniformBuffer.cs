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
        private readonly List<ModelUniform> _transforms = new();
        private Matrix4x4 _lastUploaded = Matrix4x4.Identity;
        private readonly List<IDisposable> _staleResources = new();

        public ModelUniformBuffer(GraphicsDevice gd, int initialCapacity = 32768)
        {
            _gd = gd;
            _stride = (uint)Marshal.SizeOf<ModelUniform>();

            var factory = gd.ResourceFactory;

            _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription(
                    "ModelTransforms",
                    ResourceKind.StructuredBufferReadWrite,
                    ShaderStages.Vertex | ShaderStages.Compute)));
            
            // Console.WriteLine("[ModelUniformBuffer] Created layout with Vertex|Compute support.");

            // Create buffer large enough for multiple transforms
            _buffer = factory.CreateBuffer(new BufferDescription(
                _stride * (uint)initialCapacity,
                BufferUsage.StructuredBufferReadWrite, (uint)sizeof(float)*16));

            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));
        }

        public int Allocate(Matrix4x4 transform)
        {
            EnsureCapacity(_transforms.Count + 1);
            
            var uniformMatrix = transform;
            var uniform = new ModelUniform(uniformMatrix);
            int index = _transforms.Count;
            _transforms.Add(uniform);
            _lastUploaded = transform;
            
            _gd.UpdateBuffer(_buffer, _stride * (uint)index, ref uniform);
            return index;
        }

        public void UpdateTransform(int index, Matrix4x4 transform)
        {
            if ((uint)index >= _transforms.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var uniformMatrix = transform;
            var uniform = new ModelUniform(uniformMatrix);
            _transforms[index] = uniform;
            
            // Write to the correct offset for this index
            _gd.UpdateBuffer(_buffer, _stride * (uint)index, ref uniform);
            _lastUploaded = transform;
        }

        public void UpdateTransform(CommandList cl, int index, Matrix4x4 transform)
        {
            if ((uint)index >= _transforms.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var uniformMatrix = transform;
            var uniform = new ModelUniform(uniformMatrix);
            _transforms[index] = uniform;
            
            // Write to the correct offset for this index
            _gd.UpdateBuffer(_buffer, _stride * (uint)index, ref uniform);
            _lastUploaded = transform;
        }

        public void UpdateAll(ReadOnlySpan<ModelUniform> uniforms)
        {
            _transforms.Clear();
            if (uniforms.Length == 0) return;

            EnsureCapacity(uniforms.Length);
            
            var array = uniforms.ToArray();
            _transforms.AddRange(array);
            
            _gd.UpdateBuffer(_buffer, 0, array);
        }

        public void EnsureCapacity(int capacity)
        {
            uint neededBytes = (uint)(capacity * _stride);
            if (_buffer.SizeInBytes < neededBytes)
            {
                _gd.WaitForIdle();
                uint newSize = Math.Max(neededBytes, (uint)(_buffer.SizeInBytes * 2));
                
                // Dispose immediately after idle
                _buffer.Dispose();
                _resourceSet.Dispose();
                
                _buffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(
                    newSize, 
                    BufferUsage.StructuredBufferReadWrite, 
                    _stride)); // StructureByteStride
                
                _resourceSet = _gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));
            }
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

            return _transforms[index].ModelMatrix;
        }

        public void Bind(CommandList cl, int index)
        {
            if ((uint)index >= _transforms.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var uniform = _transforms[index];
            // Write to the correct offset for this index
            cl.UpdateBuffer(_buffer, _stride * (uint)index, ref uniform);
            _lastUploaded = uniform.ModelMatrix;
        }

        public Matrix4x4 GetLastUploadedMatrix() => _lastUploaded;

        public void Dispose()
        {
            _buffer.Dispose();
            _layout.Dispose();
            _resourceSet.Dispose();
            
            foreach (var resource in _staleResources)
            {
                resource.Dispose();
            }
            _staleResources.Clear();
        }
    }
}
