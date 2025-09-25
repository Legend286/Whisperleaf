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
        bool enableBlend = false)
    {
        var factory = gd.ResourceFactory;

        string vertexCode = File.ReadAllText(vertexPath);
        string fragmentCode = File.ReadAllText(fragmentPath);

        Shader[] shaders = factory.CreateFromSpirv(
            new ShaderDescription(
                ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main"),
            new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main"));
        
        
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
            ResourceLayouts = System.Array.Empty<ResourceLayout>(),
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
                shaders: shaders),
            Outputs = target.OutputDescription,
        };

        return factory.CreateGraphicsPipeline(pd);
    }
}