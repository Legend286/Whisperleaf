using System;
using Veldrid;

namespace Whisperleaf.Graphics.RenderPasses;

public class VolumetricPass : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;
    private Pipeline _pipeline;
    private ResourceLayout _layout;
    private ResourceSet _resourceSet;
    private Sampler _depthSampler;
    private Framebuffer _outputFramebuffer;
    
    // Cached resources for validation
    private TextureView _cachedOutputView;
    private TextureView _cachedDepthView;
    private TextureView _cachedLightGridView;
    private DeviceBuffer _cachedLightDataBuffer;
    private DeviceBuffer _cachedLightParamsBuffer;
    private DeviceBuffer _cachedLightIndicesBuffer;
    private DeviceBuffer _cachedShadowDataBuffer;

    public VolumetricPass(GraphicsDevice gd)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;

        var vertShader = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/FullScreen.vert");
        var fragShader = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/Volumetric.frag");

        _layout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LightDataBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LightParams", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LightGridTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LightIndices", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DepthTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DepthSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ShadowAtlas", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ShadowDataBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
            // Binding 10 (Output) removed - we render to Framebuffer now.
            new ResourceLayoutElementDescription("LightGridSampler", ResourceKind.Sampler, ShaderStages.Fragment) // Binding 11
        ));

        _pipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAdditiveBlend,
            DepthStencilStateDescription.Disabled,
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(
                new VertexLayoutDescription[] {}, 
                new[] { vertShader, fragShader }),
            new[] { _layout },
            new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.R16_G16_B16_A16_Float))
        ));

        _depthSampler = _factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint, null, 0, 0, 100, 0, SamplerBorderColor.OpaqueWhite));
    }

    public void UpdateResources(TextureView outputView, TextureView depthView, GltfPass gltfPass, TextureView lightGridView, DeviceBuffer lightIndicesBuffer)
    {
        _cachedOutputView = outputView;
        _cachedDepthView = depthView;
        _cachedLightGridView = lightGridView;
        
        _outputFramebuffer?.Dispose();
        _outputFramebuffer = _factory.CreateFramebuffer(new FramebufferDescription(null, outputView.Target)); 
        
        UpdateResourceSet(gltfPass);
    }

    private void UpdateResourceSet(GltfPass gltfPass)
    {
        _resourceSet?.Dispose();
        
        if (gltfPass.CameraBuffer.Buffer == null || gltfPass.ShadowAtlas == null) return;

        _cachedLightDataBuffer = gltfPass.LightDataBuffer;
        _cachedLightParamsBuffer = gltfPass.LightParamsBuffer;
        _cachedLightIndicesBuffer = gltfPass.LightIndexListBuffer;
        _cachedShadowDataBuffer = gltfPass.ShadowDataBuffer;

        _resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _layout,
            gltfPass.CameraBuffer.Buffer, 
            _cachedLightDataBuffer,     
            _cachedLightParamsBuffer,   
            _cachedLightGridView,                
            _cachedLightIndicesBuffer,           
            _cachedDepthView,                    
            _depthSampler,                
            gltfPass.ShadowAtlas.TextureView, 
            gltfPass.ShadowAtlas.ShadowSampler, 
            _cachedShadowDataBuffer,    
            _depthSampler 
        ));
    }

    public void Execute(CommandList cl, GltfPass gltfPass)
    {
        if (_outputFramebuffer == null) return;

        if (gltfPass.LightDataBuffer != _cachedLightDataBuffer ||
            gltfPass.LightParamsBuffer != _cachedLightParamsBuffer ||
            gltfPass.LightIndexListBuffer != _cachedLightIndicesBuffer ||
            gltfPass.ShadowDataBuffer != _cachedShadowDataBuffer)
        {
            UpdateResourceSet(gltfPass);
        }

        if (_resourceSet == null) return;

        cl.SetFramebuffer(_outputFramebuffer);
        cl.SetFullViewports();
        cl.SetFullScissorRects();
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _resourceSet);
        cl.Draw(3, 1, 0, 0);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _layout.Dispose();
        _depthSampler.Dispose();
        _resourceSet?.Dispose();
        _outputFramebuffer?.Dispose();
    }
}