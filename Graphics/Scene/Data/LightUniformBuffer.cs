using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Veldrid;

namespace Whisperleaf.Graphics.Scene.Data;

public class LightUniformBuffer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceLayout _layout;
    private DeviceBuffer _buffer;
    private ResourceSet _resourceSet;
    private readonly uint _stride;
    private readonly List<LightUniform> _lights = new();
    
    // We also need a buffer to store the count of lights if we are using a structured buffer without a separate uniform for count,
    // but usually we pass count as a separate uniform or just use a fixed max size for now.
    // For simplicity, let's pass a small uniform buffer for global params (light count).
    private DeviceBuffer _paramBuffer;
    private ResourceLayout _paramLayout;
    private ResourceSet _paramResourceSet;

    public LightUniformBuffer(GraphicsDevice gd, int initialCapacity = 1024)
    {
        _gd = gd;
        _stride = (uint)Marshal.SizeOf<LightUniform>();
        var factory = gd.ResourceFactory;

        // 1. Light Data Buffer (Structured)
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription(
                "LightData",
                ResourceKind.StructuredBufferReadOnly,
                ShaderStages.Fragment | ShaderStages.Compute)));

        _buffer = factory.CreateBuffer(new BufferDescription(
            _stride * (uint)initialCapacity,
            BufferUsage.StructuredBufferReadOnly, 
            _stride));

        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));

        // 2. Light Count Uniform
        _paramLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("LightParams", ResourceKind.UniformBuffer, ShaderStages.Fragment | ShaderStages.Compute)
        ));
        _paramBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer)); // 16 bytes min alignment usually
        _paramResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_paramLayout, _paramBuffer));
    }

    public void AddLight(LightUniform light)
    {
        _lights.Add(light);
    }

    public void Clear()
    {
        _lights.Clear();
    }

    public void UpdateGPU()
    {
        if (_lights.Count == 0)
        {
            // Update count to 0
            _gd.UpdateBuffer(_paramBuffer, 0, 0u);
            return;
        }

        // Ensure buffer size
        uint sizeNeeded = (uint)_lights.Count * _stride;
        if (_buffer.SizeInBytes < sizeNeeded)
        {
            _buffer.Dispose();
            // Double capacity or match needed
            uint newSize = Math.Max(sizeNeeded, _buffer.SizeInBytes * 2);
            _buffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(newSize, BufferUsage.StructuredBufferReadOnly, _stride));
            
            // Recreate resource set
            _resourceSet.Dispose();
            _resourceSet = _gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));
        }

        _gd.UpdateBuffer(_buffer, 0, _lights.ToArray());
        _gd.UpdateBuffer(_paramBuffer, 0, (uint)_lights.Count);
    }

    public ResourceLayout Layout => _layout;
    public ResourceSet ResourceSet => _resourceSet;
    
    public ResourceLayout ParamLayout => _paramLayout;
    public ResourceSet ParamResourceSet => _paramResourceSet;

    public void Dispose()
    {
        _buffer.Dispose();
        _layout.Dispose();
        _resourceSet.Dispose();
        _paramBuffer.Dispose();
        _paramLayout.Dispose();
        _paramResourceSet.Dispose();
    }
}
