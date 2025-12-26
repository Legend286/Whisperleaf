using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Whisperleaf.Graphics.Shadows;

namespace Whisperleaf.Graphics.Scene.Data;

public class CsmUniformBuffer : IDisposable
{
    private readonly DeviceBuffer _buffer;
    private readonly ResourceLayout _layout;
    private readonly ResourceSet _resourceSet;

    public CsmUniformBuffer(GraphicsDevice gd, TextureView csmTextureView, Sampler shadowSampler)
    {
        var factory = gd.ResourceFactory;

        _buffer = factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<CsmUniform>(),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CsmMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("CsmSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("CsmUniform", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, 
            csmTextureView, 
            shadowSampler,
            _buffer));
    }

    public void Update(GraphicsDevice gd, CsmAtlas atlas)
    {
        var data = new CsmUniform
        {
            CascadeViewProj0 = atlas.GetViewProj(0),
            CascadeViewProj1 = atlas.GetViewProj(1),
            CascadeViewProj2 = atlas.GetViewProj(2),
            CascadeViewProj3 = atlas.GetViewProj(3),
            CascadeSplits = new Vector4(atlas.GetSplit(0), atlas.GetSplit(1), atlas.GetSplit(2), atlas.GetSplit(3))
        };
        gd.UpdateBuffer(_buffer, 0, ref data);
    }

    public ResourceLayout Layout => _layout;
    public ResourceSet ResourceSet => _resourceSet;

    public void Dispose()
    {
        _buffer.Dispose();
        _layout.Dispose();
        _resourceSet.Dispose();
    }
}
