using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Whisperleaf.Graphics.Data;

namespace Whisperleaf.Graphics.RenderPasses;

public class BloomPass : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;

    private readonly Pipeline _thresholdPipeline;
    private readonly Pipeline _downsamplePipeline;
    private readonly Pipeline _upsamplePipeline;
    private readonly Pipeline _combinePipeline;

    private readonly ResourceLayout _thresholdLayout;
    private readonly ResourceLayout _downsampleLayout;
    private readonly ResourceLayout _upsampleLayout;
    private readonly ResourceLayout _combineLayout;
    private readonly ResourceLayout _thresholdParamsLayout;
    private readonly ResourceLayout _combineParamsLayout;

    private readonly Sampler _clampSampler;
    private DeviceBuffer _thresholdParamsBuffer;
    private DeviceBuffer _combineParamsBuffer;
    private ResourceSet _thresholdParamsSet;
    private ResourceSet _combineParamsSet;

    private struct ThresholdParams {
        public float Threshold;
        public float SoftKnee;
        private Vector2 _pad;
    }

    private struct CombineParams {
        public float BloomIntensity;
        public float Exposure;
        private Vector2 _pad;
    }

    private struct DownsampleParams {
        public Vector2 TexelSize;
        private Vector2 _pad;
    }

    private struct UpsampleParams {
        public float FilterRadius;
        private Vector3 _pad;
    }

    private class BloomLevel {
        public Texture Texture;
        public TextureView View;
        public Framebuffer Framebuffer;
        public ResourceSet DownsampleSet; // From previous level TO this level
        public ResourceSet UpsampleSet;   // From next level TO this level
        public DeviceBuffer ParamsBuffer;

        public void Dispose() {
            Texture.Dispose();
            View.Dispose();
            Framebuffer.Dispose();
            DownsampleSet?.Dispose();
            UpsampleSet?.Dispose();
            ParamsBuffer.Dispose();
        }
    }

    private readonly List<BloomLevel> _levels = new();
    private Texture _thresholdTexture;
    private TextureView _thresholdView;
    private Framebuffer _thresholdFramebuffer;
    private ResourceSet _thresholdInputSet;
    private ResourceSet _combineSet;
    private TextureView? _lastSceneView;

    public bool Enabled { get; set; } = true;
    public float Threshold { get; set; } = 1.0f;
    public float SoftKnee { get; set; } = 0.5f;
    public float Intensity { get; set; } = 1.0f;
    public float Exposure { get; set; } = 1.0f;
    public float FilterRadius { get; set; } = 0.005f;

    private uint _width, _height;

    public BloomPass(GraphicsDevice gd, OutputDescription combineOutputDesc) {
        _gd = gd;
        _factory = gd.ResourceFactory;

        _clampSampler = _factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear, ComparisonKind.Never, (uint)0, (uint)0, (uint)0, 0, SamplerBorderColor.OpaqueBlack));

        // 1. Layouts
        _thresholdLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("InputTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
        ));

        _thresholdParamsLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ThresholdParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        _downsampleLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("InputTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DownsampleParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        _upsampleLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("InputTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("UpsampleParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        _combineLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BloomTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
        ));

        _combineParamsLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CombineParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        // 2. Pipelines
        var bloomInternalOutputDesc = new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.R16_G16_B16_A16_Float));

        _thresholdPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(Array.Empty<VertexLayoutDescription>(), ShaderCache.GetShaderPair(_gd, "Graphics/Shaders/Skybox.vert", "Graphics/Shaders/Bloom/BloomThreshold.frag")),
            new[] { _thresholdLayout, _thresholdParamsLayout }, bloomInternalOutputDesc));

        _downsamplePipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(Array.Empty<VertexLayoutDescription>(), ShaderCache.GetShaderPair(_gd, "Graphics/Shaders/Skybox.vert", "Graphics/Shaders/Bloom/BloomDownsample.frag")),
            new[] { _downsampleLayout }, bloomInternalOutputDesc));

        _upsamplePipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAdditiveBlend, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(Array.Empty<VertexLayoutDescription>(), ShaderCache.GetShaderPair(_gd, "Graphics/Shaders/Skybox.vert", "Graphics/Shaders/Bloom/BloomUpsample.frag")),
            new[] { _upsampleLayout }, bloomInternalOutputDesc));

        _combinePipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(Array.Empty<VertexLayoutDescription>(), ShaderCache.GetShaderPair(_gd, "Graphics/Shaders/Skybox.vert", "Graphics/Shaders/Bloom/BloomCombine.frag")),
            new[] { _combineLayout, _combineParamsLayout }, 
            combineOutputDesc));

        // 3. Global Buffers
        _thresholdParamsBuffer = _factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        _thresholdParamsSet = _factory.CreateResourceSet(new ResourceSetDescription(_thresholdParamsLayout, _thresholdParamsBuffer));
        
        _combineParamsBuffer = _factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        _combineParamsSet = _factory.CreateResourceSet(new ResourceSetDescription(_combineParamsLayout, _combineParamsBuffer));
    }

    public void Resize(uint width, uint height) {
        if (width == _width && height == _height) return;
        _gd.WaitForIdle();
        _width = width; _height = height;

        foreach (var level in _levels) level.Dispose();
        _levels.Clear();
        _thresholdTexture?.Dispose();
        _thresholdView?.Dispose();
        _thresholdFramebuffer?.Dispose();
        _thresholdInputSet?.Dispose();
        _combineSet?.Dispose();
        _thresholdInputSet = null;
        _combineSet = null;
        _lastSceneView = null;

        // Create Threshold Resources
        _thresholdTexture = _factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _thresholdView = _factory.CreateTextureView(_thresholdTexture);
        _thresholdFramebuffer = _factory.CreateFramebuffer(new FramebufferDescription(null, _thresholdTexture));

        // Create Bloom Levels (6 levels)
        uint curW = width / 2;
        uint curH = height / 2;
        TextureView lastView = _thresholdView;

        for (int i = 0; i < 6; i++) {
            if (curW < 2 || curH < 2) break;
            
            var level = new BloomLevel();
            level.Texture = _factory.CreateTexture(TextureDescription.Texture2D(curW, curH, 1, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
            level.View = _factory.CreateTextureView(level.Texture);
            level.Framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(null, level.Texture));
            level.ParamsBuffer = _factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            
            // ResourceSet to downsample FROM lastView INTO this level
            level.DownsampleSet = _factory.CreateResourceSet(new ResourceSetDescription(_downsampleLayout, _clampSampler, lastView, level.ParamsBuffer));

            _levels.Add(level);
            lastView = level.View;
            curW /= 2; curH /= 2;
        }

        // Create UpsampleSets now that all levels exist
        for (int i = 0; i < _levels.Count - 1; i++)
        {
             // To upsample FROM level[i+1] INTO level[i]
             _levels[i].UpsampleSet = _factory.CreateResourceSet(new ResourceSetDescription(_upsampleLayout, _clampSampler, _levels[i+1].View, _levels[i].ParamsBuffer));
        }
    }

    public void Render(CommandList cl, TextureView sceneView, Framebuffer outputFb) {
        if (!Enabled || _levels.Count == 0 || sceneView == null) return;

        // 1. Threshold
        cl.UpdateBuffer(_thresholdParamsBuffer, 0, new ThresholdParams { Threshold = Threshold, SoftKnee = SoftKnee });
        if (_thresholdInputSet == null || _lastSceneView != sceneView) {
            _thresholdInputSet?.Dispose();
            _thresholdInputSet = _factory.CreateResourceSet(new ResourceSetDescription(_thresholdLayout, _clampSampler, sceneView));
            
            _combineSet?.Dispose();
            _combineSet = _factory.CreateResourceSet(new ResourceSetDescription(_combineLayout, _clampSampler, sceneView, _levels[0].View));
            
            _lastSceneView = sceneView;
        }

        cl.SetFramebuffer(_thresholdFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, _width, _height, 0, 1));
        cl.SetPipeline(_thresholdPipeline);
        cl.SetGraphicsResourceSet(0, _thresholdInputSet);
        cl.SetGraphicsResourceSet(1, _thresholdParamsSet);
        cl.Draw(3);

        // 2. Downsample
        for (int i = 0; i < _levels.Count; i++) {
            var level = _levels[i];
            uint inputW = (i == 0) ? _width : _levels[i-1].Texture.Width;
            uint inputH = (i == 0) ? _height : _levels[i-1].Texture.Height;

            cl.UpdateBuffer(level.ParamsBuffer, 0, new DownsampleParams { TexelSize = new Vector2(1.0f / inputW, 1.0f / inputH) });
            
            cl.SetFramebuffer(level.Framebuffer);
            cl.SetViewport(0, new Viewport(0, 0, level.Texture.Width, level.Texture.Height, 0, 1));
            cl.SetPipeline(_downsamplePipeline);
            cl.SetGraphicsResourceSet(0, level.DownsampleSet);
            cl.Draw(3);
        }

        // 3. Upsample (Additive)
        for (int i = _levels.Count - 2; i >= 0; i--) {
            var level = _levels[i];
            cl.UpdateBuffer(level.ParamsBuffer, 0, new UpsampleParams { FilterRadius = FilterRadius });

            cl.SetFramebuffer(level.Framebuffer);
            cl.SetViewport(0, new Viewport(0, 0, level.Texture.Width, level.Texture.Height, 0, 1));
            cl.SetPipeline(_upsamplePipeline);
            cl.SetGraphicsResourceSet(0, level.UpsampleSet);
            cl.Draw(3);
        }

        // 4. Combine
        cl.UpdateBuffer(_combineParamsBuffer, 0, new CombineParams { BloomIntensity = Intensity, Exposure = Exposure });

        cl.SetFramebuffer(outputFb);
        cl.SetViewport(0, new Viewport(0, 0, outputFb.Width, outputFb.Height, 0, 1));
        cl.SetPipeline(_combinePipeline);
        cl.SetGraphicsResourceSet(0, _combineSet);
        cl.SetGraphicsResourceSet(1, _combineParamsSet);
        cl.Draw(3);
    }

    public void Dispose() {
        _clampSampler.Dispose();
        _thresholdPipeline.Dispose();
        _downsamplePipeline.Dispose();
        _upsamplePipeline.Dispose();
        _combinePipeline.Dispose();
        _thresholdLayout.Dispose();
        _downsampleLayout.Dispose();
        _upsampleLayout.Dispose();
        _combineLayout.Dispose();
        _thresholdParamsLayout.Dispose();
        _combineParamsLayout.Dispose();
        _thresholdParamsBuffer.Dispose();
        _combineParamsBuffer.Dispose();
        _thresholdParamsSet.Dispose();
        _combineParamsSet.Dispose();
        _thresholdTexture?.Dispose();
        _thresholdView?.Dispose();
        _thresholdFramebuffer?.Dispose();
        _thresholdInputSet?.Dispose();
        _combineSet?.Dispose();
        foreach (var level in _levels) level.Dispose();
    }
}
