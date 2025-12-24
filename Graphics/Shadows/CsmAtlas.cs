using System;
using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.Shadows;

public class CsmAtlas : IDisposable
{
    private readonly GraphicsDevice _gd;
    private Texture _textureArray;
    private TextureView _textureView;
    private ResourceSet _resourceSet;
    private ResourceLayout _layout;

    public const int CascadeCount = 4;
    private const int TextureSize = 4096;

    private readonly Framebuffer[] _cascadeFramebuffers;
    private readonly Matrix4x4[] _cascadeViewProjs = new Matrix4x4[CascadeCount];
    private readonly float[] _cascadeSplits = new float[CascadeCount];
    private Sampler _shadowSampler;

    public CsmAtlas(GraphicsDevice gd)
    {
        _gd = gd;
        _cascadeFramebuffers = new Framebuffer[CascadeCount];
        CreateResources();
        CreateFramebuffers();
    }

    private void CreateResources()
    {
        var factory = _gd.ResourceFactory;

        _textureArray = factory.CreateTexture(new TextureDescription(
            TextureSize, TextureSize, 1, 1, CascadeCount,
            PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil | TextureUsage.Sampled, TextureType.Texture2D));

        _textureView = factory.CreateTextureView(_textureArray);

        _shadowSampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            ComparisonKind.LessEqual, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CsmMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("CsmSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));

        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _textureView, _shadowSampler));
    }

    private void CreateFramebuffers()
    {
        var factory = _gd.ResourceFactory;
        for (int i = 0; i < CascadeCount; i++)
        {
            _cascadeFramebuffers[i] = factory.CreateFramebuffer(new FramebufferDescription(
                new FramebufferAttachmentDescription(_textureArray, (uint)i),
                Array.Empty<FramebufferAttachmentDescription>()));
        }
    }

    public void UpdateCascades(Camera camera, Vector3 lightDir)
    {
        float near = camera.Near;
        float far = Math.Min(camera.Far, 150.0f); 
        
        // Simple logarithmic splits
        float lambda = 0.935f;
        for (int i = 0; i < CascadeCount; i++)
        {
            float p = (float)(i + 1) / CascadeCount;
            float log = near * MathF.Pow(far / near, p);
            float uniform = near + (far - near) * p;
            _cascadeSplits[i] = lambda * log + (1.0f - lambda) * uniform;
        }

        float lastSplit = near;
        for (int i = 0; i < CascadeCount; i++)
        {
            _cascadeViewProjs[i] = CalculateCascadeMatrix(camera, lastSplit, _cascadeSplits[i], lightDir);
            lastSplit = _cascadeSplits[i];
        }
    }

    private Matrix4x4 CalculateCascadeMatrix(Camera camera, float near, float far, Vector3 lightDir)
    {
        // 1. Get Frustum corners in world space
        float fovRadians = camera.Fov * (MathF.PI / 180.0f);
        float tanHalfFov = MathF.Tan(fovRadians * 0.5f);
        float xNear = near * tanHalfFov * camera.AspectRatio;
        float yNear = near * tanHalfFov;
        float xFar = far * tanHalfFov * camera.AspectRatio;
        float yFar = far * tanHalfFov;

        Vector3[] corners = new Vector3[8];
        corners[0] = new Vector3(-xNear,  yNear, -near);
        corners[1] = new Vector3( xNear,  yNear, -near);
        corners[2] = new Vector3( xNear, -yNear, -near);
        corners[3] = new Vector3(-xNear, -yNear, -near);
        corners[4] = new Vector3(-xFar,  yFar, -far);
        corners[5] = new Vector3( xFar,  yFar, -far);
        corners[6] = new Vector3( xFar, -yFar, -far);
        corners[7] = new Vector3(-xFar, -yFar, -far);

        Matrix4x4.Invert(camera.ViewMatrix, out var invView);
        for(int i = 0; i < 8; i++) corners[i] = Vector3.Transform(corners[i], invView);

        // 2. Bounding Sphere (invariant to rotation)
        Vector3 center = Vector3.Zero;
        foreach (var p in corners) center += p;
        center /= 8.0f;

        float radius = 0f;
        foreach (var p in corners) radius = Math.Max(radius, Vector3.Distance(center, p));
        radius = MathF.Ceiling(radius * 16.0f) / 16.0f; 

        // 3. Stable View Matrix
        Vector3 up = Vector3.UnitY;
        if (Math.Abs(Vector3.Dot(lightDir, up)) > 0.99f) up = Vector3.UnitZ;
        var lightView = Matrix4x4.CreateLookAt(center - lightDir * radius, center, up);

        // 4. Stable Projection with Texel Snapping
        float worldUnitsPerTexel = (radius * 2.0f) / TextureSize;
        
        var lightProj = Matrix4x4.CreateOrthographicOffCenter(-radius, radius, -radius, radius, -radius * 10.0f, radius * 10.0f);
        
        // Snap the resulting matrix
        Matrix4x4 shadowMatrix = lightView * lightProj;
        Vector4 shadowOrigin = new Vector4(0, 0, 0, 1.0f);
        shadowOrigin = Vector4.Transform(shadowOrigin, shadowMatrix);
        shadowOrigin *= (TextureSize / 2.0f);

        Vector4 roundedOrigin = new Vector4(MathF.Round(shadowOrigin.X), MathF.Round(shadowOrigin.Y), MathF.Round(shadowOrigin.Z), MathF.Round(shadowOrigin.W));
        Vector4 roundOffset = roundedOrigin - shadowOrigin;
        roundOffset *= (2.0f / TextureSize);
        roundOffset.Z = 0.0f;
        roundOffset.W = 0.0f;

        lightProj.M41 += roundOffset.X;
        lightProj.M42 += roundOffset.Y;
        
        if (_gd.IsDepthRangeZeroToOne)
        {
            var clipMat = new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0.5f, 0, 0, 0, 0.5f, 1);
            lightProj = lightProj * clipMat;
        }
        if (_gd.IsClipSpaceYInverted) lightProj.M22 *= -1;

        return lightView * lightProj;
    }

    public ResourceSet ResourceSet => _resourceSet;
    public ResourceLayout Layout => _layout;
    public TextureView TextureView => _textureView;
    public Sampler ShadowSampler => _shadowSampler;
    public Framebuffer GetFramebuffer(int cascade) => _cascadeFramebuffers[cascade];
    public Matrix4x4 GetViewProj(int cascade) => _cascadeViewProjs[cascade];
    public float GetSplit(int cascade) => _cascadeSplits[cascade];

    public void Dispose()
    {
        foreach (var fb in _cascadeFramebuffers) fb?.Dispose();
        _textureArray.Dispose();
        _textureView.Dispose();
        _shadowSampler.Dispose();
        _layout.Dispose();
        _resourceSet.Dispose();
    }
}
