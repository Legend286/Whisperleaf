using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;
using Whisperleaf.AssetPipeline;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.RenderPasses;

public class SkyboxPass : IRenderPass
{
    private readonly GraphicsDevice _gd;
    private readonly Pipeline _pipeline;
    private readonly DeviceBuffer _skyBuffer;
    private readonly ResourceSet _skyResourceSet;
    private readonly CameraUniformBuffer _cameraBuffer; // Shared reference

    [StructLayout(LayoutKind.Sequential)]
    private struct SkyParams
    {
        public Vector3 SunDirection;
        public float SunIntensity;
        public Vector3 PlanetCenter;
        public float PlanetRadius;
        public Vector3 RayleighScattering;
        public float AtmosphereRadius;
        public Vector3 MieScattering;
        public float MieG;
    }

    private SkyParams _params;

    public SkyboxPass(GraphicsDevice gd, CameraUniformBuffer cameraBuffer, OutputDescription outputDescription)
    {
        _gd = gd;
        _cameraBuffer = cameraBuffer;
        var factory = gd.ResourceFactory;

        // Default Earth-like parameters
        // Scattering coefficients are usually e-6. 
        // Example: (5.5e-6, 13.0e-6, 22.4e-6) for R, G, B
        _params = new SkyParams
        {
            SunDirection = Vector3.Normalize(new Vector3(0.0f, 1.0f, -0.5f)),
            SunIntensity = 22.0f,
            PlanetCenter = new Vector3(0, -6360000, 0), // 6,360 km radius
            PlanetRadius = 6360000,
            RayleighScattering = new Vector3(5.5e-6f, 13.0e-6f, 22.4e-6f),
            AtmosphereRadius = 6420000, // +60km atmosphere
            MieScattering = new Vector3(21e-6f, 21e-6f, 21e-6f),
            MieG = 0.76f
        };

        // Fullscreen triangle, no vertex buffer needed
        
        _skyBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<SkyParams>(), BufferUsage.UniformBuffer));
        
        var skyLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SkyParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        _skyResourceSet = factory.CreateResourceSet(new ResourceSetDescription(skyLayout, _skyBuffer));

        var shaderSet = new ShaderSetDescription(
            new VertexLayoutDescription[] {}, // No vertex input
            ShaderCache.GetShaderPair(gd, "Graphics/Shaders/Skybox.vert", "Graphics/Shaders/Skybox.frag")
        );

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.DepthOnlyLessEqual, // Draw where z <= 1.0 (sky is at 1.0)
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            shaderSet,
            new[] { cameraBuffer.Layout, skyLayout },
            outputDescription
        ));
    }

    public void UpdateSun(Vector3 direction, float intensity = 22.0f)
    {
        _params.SunDirection = Vector3.Normalize(direction);
        _params.SunIntensity = intensity;
    }

    public void Render(GraphicsDevice gd, CommandList cl, Camera? camera, Vector2 screenSize, int debugMode)
    {
        cl.UpdateBuffer(_skyBuffer, 0, ref _params);
        
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(1, _skyResourceSet);
        
        cl.Draw(3, 1, 0, 0); // Fullscreen triangle
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _skyBuffer.Dispose();
        _skyResourceSet.Dispose();
    }
}

