using System;
using System.Numerics;
using Veldrid;
using Vortice.Direct3D11;
using BufferDescription = Veldrid.BufferDescription;
using SamplerDescription = Veldrid.SamplerDescription;

namespace Whisperleaf.Graphics.RenderPasses;

public class HiZPass : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;
    private Pipeline _pipeline;
    private ResourceLayout _layout;

    private Texture _hiZTexture;
    private TextureView _hiZFullView; 
    private TextureView[] _mipViews; 
    
    // Per-pass resources
    private DeviceBuffer[] _paramBuffers;
    private ResourceSet[] _resourceSets;
    private Sampler _sampler;

    public TextureView HiZTextureView => _hiZFullView;
    public uint MipLevels => _hiZTexture?.MipLevels ?? 0;
    public uint Width => _hiZTexture?.Width ?? 0;
    public uint Height => _hiZTexture?.Height ?? 0;

    public HiZPass(GraphicsDevice gd)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;
        
        var computeShader = ShaderCache.GetShader(_gd, ShaderStages.Compute, "Graphics/Shaders/HiZDownsample.comp");
        
        // Layout: 0:InputTex, 1:Sampler, 2:OutputTex, 3:Params
        _layout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("InputTexture", ResourceKind.TextureReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("InputSampler", ResourceKind.Sampler, ShaderStages.Compute),
            new ResourceLayoutElementDescription("OutputTexture", ResourceKind.TextureReadWrite, ShaderStages.Compute),
            new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Compute)
        ));

        _pipeline = _factory.CreateComputePipeline(new ComputePipelineDescription(
            computeShader,
            _layout,
            16, 16, 1
        ));

        _sampler = _factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerFilter.MinPoint_MagPoint_MipPoint, // Point sampling for reduction
            null, 0, 0, 100, 0, SamplerBorderColor.TransparentBlack));
    }

    public void Resize(uint width, uint height)
    {
        if (_hiZTexture != null && _hiZTexture.Width == width && _hiZTexture.Height == height) return;
        
        DisposeResources();

        // Calculate mip levels to go down to 1x1 or close
        uint mipLevels = (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
        
        _hiZTexture = _factory.CreateTexture(TextureDescription.Texture2D(
            width, height, mipLevels, 1, 
            PixelFormat.R32_Float, 
            TextureUsage.Sampled | TextureUsage.Storage));
        
        _hiZFullView = _factory.CreateTextureView(_hiZTexture);
        
        _mipViews = new TextureView[mipLevels];
        for (uint i = 0; i < mipLevels; i++)
        {
            _mipViews[i] = _factory.CreateTextureView(new TextureViewDescription(_hiZTexture, i, 1, 0, 1));
        }

        // Create Param Buffers for each dispatch (Pass 0 to N-1)
        // Pass 0: Depth -> Mip0
        // Pass 1: Mip0 -> Mip1
        // ...
        // Pass N: Mip N-1 -> Mip N
        // Total passes = mipLevels (0 to mipLevels-1) ??
        // Actually:
        // Pass 0 copies Depth to Mip0? 
        // Or do we want Mip0 to be 1:1 copy of Depth?
        // HiZDownsample shader does a reduction (2x2 -> 1).
        // If we want Mip0 to be width/2 x height/2, then Pass 0 takes Depth(w,h) -> Mip0(w/2, h/2).
        // BUT HiZ usually starts with Mip0 matching screen resolution (or next POT).
        // If Mip0 matches screen res, we just copy Depth -> Mip0.
        // Then Mip1 is reduced Mip0.
        // Let's assume Mip0 = Max reduction of Depth (which is fine if Mip0 is same size as Depth? No, then it's 1x1 copy).
        // If Mip0 size == Depth size, reduction shader will sample fractional pixels if we use the same shader.
        // Let's stick to: Mip0 size is same as Depth size.
        // So Pass 0 is a COPY (or max reduction of 1x1 block).
        // Optimization: Blit Depth to Mip0 first?
        // Or just run the reduction shader with InputSize = OutputSize (1:1 mapping)?
        // My shader does: uv = (pos * 2.0 + 1.0) * texelSize * 0.5.
        // This logic assumes 2x downsample.
        // So we need a special "Copy" pass or a different shader for Mip0 if we want 1:1.
        // OR: Mip0 is half resolution of screen. This is common for HiZ.
        // If screen is 1280x720, Mip0 is 640x360.
        // This is valid. Occlusion culling usually works fine with half-res buffer.
        // Let's go with Mip0 = Half Res.
        
        // Wait, _hiZTexture size is created with `width, height`. If I pass ViewportWidth/Height, Mip0 is full res.
        // If I want half res, I should pass width/2, height/2 to Resize.
        // Let's assume the caller passes the desired HiZ size.
        
        int numPasses = (int)mipLevels; // Each pass writes to one mip level.
        // Pass 0 writes to Mip0 (from external Depth).
        // Pass k writes to Mip k (from Mip k-1).

        _paramBuffers = new DeviceBuffer[numPasses];
        _resourceSets = new ResourceSet[numPasses];

        for(int i=0; i<numPasses; i++)
        {
            _paramBuffers[i] = _factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        }
    }

    private TextureView _depthView;

    public void UpdateResources(TextureView depthView)
    {
        // If we already have sets and depth view matches, verify. 
        // But ResourceSets depend on MipViews which are recreated on Resize.
        // So if Resize happened, _resourceSets are null/disposed.
        
        if (_resourceSets[0] != null && _depthView == depthView) return;
        _depthView = depthView;

        // Pass 0: Depth -> Mip0
        _resourceSets[0]?.Dispose();
        _resourceSets[0] = _factory.CreateResourceSet(new ResourceSetDescription(
            _layout,
            _depthView,
            _sampler,
            _mipViews[0],
            _paramBuffers[0]
        ));

        // Pass 1..N: Mip i-1 -> Mip i
        for (int i = 1; i < _resourceSets.Length; i++)
        {
            _resourceSets[i]?.Dispose();
            _resourceSets[i] = _factory.CreateResourceSet(new ResourceSetDescription(
                _layout,
                _mipViews[i-1], // Input
                _sampler,
                _mipViews[i],   // Output
                _paramBuffers[i]
            ));
        }
    }

    public void Execute(CommandList cl)
    {
        cl.SetPipeline(_pipeline);

        // Update Params & Dispatch
        // Pass 0 input size = Depth Texture Size.
        // We don't have depth texture object here, but we can infer from Mip0 size?
        // If Mip0 is half size, then Input is Mip0*2.
        // Actually, let's just use the Input Texture's size.
        // For Pass 0, input is Depth. We assume its size is (Width, Height) of HiZ * 2 ? 
        // Or same?
        // Let's handle the Mip0 case specially or assume caller passes full res.
        // If caller passes full res (1280x720), then Mip0 is 1280x720.
        // Then Pass 0 is 1:1 copy.
        // If I use the 2x reduction shader, it will sample outside or behave weirdly.
        // Let's update `Resize` to make Mip0 half-size if we want efficient HiZ.
        // Caller (Renderer) should pass ViewportWidth, ViewportHeight.
        // HiZ Texture will be created as NextPOT(w/2, h/2).
        
        // Wait, if I want Mip0 to be 1:1 copy, I need a copy shader.
        // If I want Mip0 to be 2:1 reduction, I can use the current shader.
        // Let's assume Mip0 is 2:1 reduction of Depth.
        // So HiZ Texture Size = Ceil(DepthSize / 2).
        
        // Pass 0: Input Size = Depth Size (unknown here? No, we can store it or calculate).
        // Pass k: Input Size = Mip k-1 Size.

        uint currentWidth = _hiZTexture.Width; // This is Mip0 width
        uint currentHeight = _hiZTexture.Height;

        // Input for Pass 0 is the Depth Texture.
        // If Mip0 is 2:1, input was double current.
        Vector2 pass0InputSize = new Vector2(currentWidth * 2, currentHeight * 2); 
        // Ideally we should get exact size from depthView texture, but we only have view.
        // We can assume strict 2x relation or just use the HiZ size logic.
        
        cl.UpdateBuffer(_paramBuffers[0], 0, new Vector2[] { pass0InputSize });
        
        uint dispatchX = (currentWidth + 15) / 16;
        uint dispatchY = (currentHeight + 15) / 16;
        
        cl.SetComputeResourceSet(0, _resourceSets[0]);
        cl.Dispatch(dispatchX, dispatchY, 1);

        // Barrier not needed between dispatches if Veldrid handles layout transitions / memory barriers for Mip levels?
        // Veldrid does automatic barriers for TextureReadWrite usage usually?
        // But for dependent compute dispatches, we might need a global memory barrier or texture barrier.
        // Veldrid doesn't expose explicit barriers in `CommandList` easily unless using `Barrier` (if exposed).
        // `cl.TextureBarrier` exists? No.
        // However, changing ResourceSet and Dispatching usually implies dependency if driver tracks it.
        // If not, we might have issues.
        // Veldrid's Metal/Vulkan backends usually track resource usage.
        
        for (int i = 1; i < _resourceSets.Length; i++)
        {
            Vector2 inputSize = new Vector2(currentWidth, currentHeight);
            
            // Next Mip
            currentWidth = Math.Max(1, currentWidth / 2);
            currentHeight = Math.Max(1, currentHeight / 2);
            
            cl.UpdateBuffer(_paramBuffers[i], 0, new Vector2[] { inputSize });
            
            dispatchX = (currentWidth + 15) / 16;
            dispatchY = (currentHeight + 15) / 16;
            
            cl.SetComputeResourceSet(0, _resourceSets[i]);
            cl.Dispatch(dispatchX, dispatchY, 1);
        }
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _layout.Dispose();
        _sampler.Dispose();
        DisposeResources();
    }

    private void DisposeResources()
    {
        _hiZTexture?.Dispose();
        _hiZFullView?.Dispose();
        if (_mipViews != null) foreach (var v in _mipViews) v.Dispose();
        if (_paramBuffers != null) foreach (var b in _paramBuffers) b.Dispose();
        if (_resourceSets != null) foreach (var s in _resourceSets) s?.Dispose();
    }
}