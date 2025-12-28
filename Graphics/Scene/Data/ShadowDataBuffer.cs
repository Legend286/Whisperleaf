using System;
using Veldrid;

namespace Whisperleaf.Graphics.Scene.Data;

public class ShadowDataBuffer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceLayout _layout;
    private DeviceBuffer _buffer;
    private ResourceSet _resourceSet;
    private readonly uint _stride;

    public ShadowDataBuffer(GraphicsDevice gd, int initialCapacity = 256)
    {
        _gd = gd;
        _stride = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ShadowData>();
        var factory = gd.ResourceFactory;

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription(
                "ShadowData",
                ResourceKind.StructuredBufferReadOnly,
                ShaderStages.Fragment)));

        _buffer = factory.CreateBuffer(new BufferDescription(
            _stride * (uint)initialCapacity,
            BufferUsage.StructuredBufferReadOnly,
            _stride));

        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));
    }

    public void Update(ShadowData[] data)
    {
        if (data.Length == 0) return;

        uint sizeNeeded = (uint)data.Length * _stride;
        if (_buffer.SizeInBytes < sizeNeeded)
        {
            _buffer.Dispose();
            _resourceSet.Dispose();
            
            uint newSize = Math.Max(sizeNeeded, _buffer.SizeInBytes * 2);
            _buffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(newSize, BufferUsage.StructuredBufferReadOnly, _stride));
            _resourceSet = _gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));
        }

        _gd.UpdateBuffer(_buffer, 0, data);
    }

    public ResourceLayout Layout => _layout;
    public ResourceSet ResourceSet => _resourceSet;
    public DeviceBuffer Buffer => _buffer;

    public void Dispose()
    {
        _buffer.Dispose();
        _layout.Dispose();
        _resourceSet.Dispose();
    }
}
