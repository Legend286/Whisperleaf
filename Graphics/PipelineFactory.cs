using Veldrid;
using Veldrid.SPIRV;

namespace Whisperleaf.Graphics;

public class PipelineFactory
{
    public static Pipeline CreatePipeline(
        GraphicsDevice gd,
        string vertexPath,
        string fragmentPath,
        VertexLayoutDescription vertexLayout,
        Framebuffer target,
        bool enableDepth = false,
        bool enableBlend = false,
        ResourceLayout[]? extraLayouts = null)
    {
        var factory = gd.ResourceFactory;
        
        Shader[] shaders = ShaderCache.GetShaderPair(gd, vertexPath, fragmentPath);

        ResourceLayout[] layouts = extraLayouts ?? System.Array.Empty<ResourceLayout>();
        
        GraphicsPipelineDescription pd = new GraphicsPipelineDescription
        {
            BlendState = enableBlend ? BlendStateDescription.SingleOverrideBlend : BlendStateDescription.SingleDisabled,
            DepthStencilState = enableDepth
                ? new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual)
                : new DepthStencilStateDescription(false, false, ComparisonKind.Always),
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None,
                PolygonFillMode.Solid,
                FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = layouts,
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
                shaders: shaders),
            Outputs = target.OutputDescription,
        };

        return factory.CreateGraphicsPipeline(pd);
    }
}