using Veldrid;
using Veldrid.SPIRV;

namespace Whisperleaf.Graphics.RenderPasses;

public class TrianglePass : IRenderPass
{
    private DeviceBuffer _vertexBuffer;
    private Pipeline _pipeline;
    private Shader[] _shaders;

    public TrianglePass(GraphicsDevice gd)
    {
        var factory = gd.ResourceFactory;

        float[] vertices = new float[]
        {
            0.0f, 0.5f, 1.0f, 0.0f, 0.0f,
            -0.5f, -0.5f, 0.0f, 1.0f, 0.0f,
            0.5f, -0.5f, 0.0f, 0.0f, 1.0f,
        };

        _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)vertices.Length * sizeof(float),
            BufferUsage.VertexBuffer));
        gd.UpdateBuffer(_vertexBuffer, 0, vertices);
        
        VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
            new VertexElementDescription("v_Color", VertexElementSemantic.Color, VertexElementFormat.Float3));


        _pipeline = PipelineFactory.CreatePipeline(gd, "Graphics/Shaders/triangle.vert",
            "Graphics/Shaders/triangle.frag",
            vertexLayout, gd.MainSwapchain.Framebuffer);
    }

    public void Render(GraphicsDevice gd, CommandList cl)
    {
        cl.Begin();
        cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
        cl.SetPipeline(_pipeline);
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.Draw(3);
        cl.End();
        
        gd.SubmitCommands(cl);
    }
}