using System;
using System.Numerics;
using Veldrid;

namespace Whisperleaf.Graphics.RenderPasses;

public class HiZPass : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;
    
    // Pass 0 (Graphics Copy: Depth -> Mip0)
    private Pipeline _pipelineCopy;
    
    // Pass 1..N (Graphics Downsample: Mip i-1 -> Mip i)
    private Pipeline _pipelineDownsample;
    private ResourceLayout _layoutCommon; // Used for both Copy and Downsample (Input: Sampled)

    private Texture _hiZTexture;
    private TextureView _hiZFullView; 
    private TextureView[] _mipViews; 
    
    // Per-pass resources
    private Framebuffer[] _mipFramebuffers; // FB for each Mip level
    private ResourceSet[] _resourceSets; // 0: Copy Input, 1..N: Downsample Input (Mip i-1)
    private Sampler _sampler;

    // Barrier resources
    private Texture _dummyBarrierTexture;
    private Framebuffer _barrierFramebuffer;
    private ResourceSet _barrierSet;

    public TextureView HiZTextureView => _hiZFullView;
    public uint MipLevels => _hiZTexture?.MipLevels ?? 0;
    public uint Width => _hiZTexture?.Width ?? 0;
    public uint Height => _hiZTexture?.Height ?? 0;

    public HiZPass(GraphicsDevice gd)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;
        
        var vertShader = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/FullScreen.vert");
        var fragCopy = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/DepthCopy.frag");
        var fragDownsample = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/HiZDownsample.frag");
        
        _layoutCommon = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("InputTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("InputSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));

        // Pipeline Copy
        _pipelineCopy = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(
                new VertexLayoutDescription[] {}, 
                new[] { vertShader, fragCopy }),
            new[] { _layoutCommon },
            new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.R32_Float))
        ));

        // Pipeline Downsample
        _pipelineDownsample = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(
                new VertexLayoutDescription[] {}, 
                new[] { vertShader, fragDownsample }),
            new[] { _layoutCommon },
            new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.R32_Float))
        ));

        _sampler = _factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerFilter.MinPoint_MagPoint_MipPoint,
            null, 0, 0, 100, 0, SamplerBorderColor.TransparentBlack));
    }

    public void Resize(uint width, uint height)
    {
        if (_hiZTexture != null && _hiZTexture.Width == width && _hiZTexture.Height == height) return;
        
        DisposeResources();

        uint mipLevels = (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
        
        _hiZTexture = _factory.CreateTexture(TextureDescription.Texture2D(
            width, height, mipLevels, 1, 
            PixelFormat.R32_Float, 
            TextureUsage.Sampled | TextureUsage.RenderTarget)); // RenderTarget for Graphics Write
        
        _hiZFullView = _factory.CreateTextureView(_hiZTexture);
        
        _mipViews = new TextureView[mipLevels];
        for (uint i = 0; i < mipLevels; i++)
        {
            _mipViews[i] = _factory.CreateTextureView(new TextureViewDescription(_hiZTexture, i, 1, 0, 1));
        }

        _mipFramebuffers = new Framebuffer[mipLevels];
        _resourceSets = new ResourceSet[mipLevels];
        
        // Create Framebuffers for ALL mips
        for (int i = 0; i < mipLevels; i++)
        {
            _mipFramebuffers[i] = _factory.CreateFramebuffer(new FramebufferDescription(null, new[] { new FramebufferAttachmentDescription(_hiZTexture, 0, (uint)i) }));
        }

        // Barrier resources
        _dummyBarrierTexture = _factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _barrierFramebuffer = _factory.CreateFramebuffer(new FramebufferDescription(null, new[] { new FramebufferAttachmentDescription(_dummyBarrierTexture, 0) }));
    }

    private Texture _depthTex;
    private TextureView _depthView;

    public void UpdateResources(Texture depthTex)
    {
        if (_resourceSets[0] != null && _depthTex == depthTex) return;
        _depthTex = depthTex;

        _depthView?.Dispose();
        _depthView = _factory.CreateTextureView(_depthTex); 
        
        if (_resourceSets != null) foreach (var s in _resourceSets) s?.Dispose();

        // Set 0: Input = Depth (for Copy)
        _resourceSets[0] = _factory.CreateResourceSet(new ResourceSetDescription(
            _layoutCommon,
            _depthView,
            _sampler
        ));

        // Sets 1..N: Input = Mip i-1 (for Downsample)
        for (int i = 1; i < _resourceSets.Length; i++)
        {
            _resourceSets[i] = _factory.CreateResourceSet(new ResourceSetDescription(
                _layoutCommon,
                _mipViews[i-1], 
                _sampler
            ));
        }

        // Barrier Set
        _barrierSet?.Dispose();
        _barrierSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _layoutCommon,
            _hiZFullView,
            _sampler
        ));
    }

    public void Execute(CommandList cl)
    {
        uint currentWidth = _hiZTexture.Width;
        uint currentHeight = _hiZTexture.Height;
        
        cl.SetFullScissorRects(); // Assuming full screen rendering for all passes (viewport changes)

        // Pass 0: Graphics Copy (Depth -> Mip0)
        cl.SetFramebuffer(_mipFramebuffers[0]);
        cl.SetViewport(0, new Viewport(0, 0, currentWidth, currentHeight, 0, 1));
        cl.SetPipeline(_pipelineCopy);
        cl.SetGraphicsResourceSet(0, _resourceSets[0]);
        cl.Draw(3, 1, 0, 0);

        // Pass 1..N: Graphics Downsample (Mip i-1 -> Mip i)
        cl.SetPipeline(_pipelineDownsample); // Same pipeline for all downsamples
        
        for (int i = 1; i < _resourceSets.Length; i++)
        {
            currentWidth = Math.Max(1, currentWidth / 2);
            currentHeight = Math.Max(1, currentHeight / 2);
            
            cl.SetFramebuffer(_mipFramebuffers[i]);
            cl.SetViewport(0, new Viewport(0, 0, currentWidth, currentHeight, 0, 1));
            cl.SetGraphicsResourceSet(0, _resourceSets[i]);
            cl.Draw(3, 1, 0, 0);
        }

        // Barrier Pass: Transition HiZ to ShaderReadOnly
        // Draw to dummy FB, sampling HiZ
        cl.SetFramebuffer(_barrierFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, 1, 1, 0, 1));
        cl.SetPipeline(_pipelineCopy); // Reuse copy pipeline (Frag reads texture)
        cl.SetGraphicsResourceSet(0, _barrierSet);
        cl.Draw(3, 1, 0, 0);
    }

    public void Dispose()
    {
        _pipelineCopy.Dispose();
        _pipelineDownsample.Dispose();
        _layoutCommon.Dispose();
        _sampler.Dispose();
        DisposeResources();
    }

    private void DisposeResources()
    {
        _depthView?.Dispose();
        _hiZTexture?.Dispose();
        _hiZFullView?.Dispose();
        _dummyBarrierTexture?.Dispose();
        _barrierFramebuffer?.Dispose();
        _barrierSet?.Dispose();
        
        if (_mipViews != null) foreach (var v in _mipViews) v.Dispose();
        if (_mipFramebuffers != null) foreach (var fb in _mipFramebuffers) fb.Dispose();
        if (_resourceSets != null) foreach (var s in _resourceSets) s?.Dispose();
    }
}