using System.Numerics;
using Veldrid;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.RenderPasses;

public class MeshPass : IRenderPass
{
    private readonly Pipeline _pipeline;
    private readonly DeviceBuffer _vertexBuffer;
    private readonly DeviceBuffer _indexBuffer;
    private readonly CameraUniformBuffer _cameraBuffer;
    private readonly int _vertexCount;

    public MeshPass(GraphicsDevice gd, Camera camera)
    {
        var factory = gd.ResourceFactory;

        // Cube with position + color
        float[] vertices =
        {
            // posX posY posZ   r g b
            -0.5f, -0.5f, -0.5f, 1, 0, 0,
            0.5f, -0.5f, -0.5f, 0, 1, 0,
            0.5f, 0.5f, -0.5f, 0, 0, 1,
            -0.5f, 0.5f, -0.5f, 1, 1, 0,

            -0.5f, -0.5f, 0.5f, 1, 0, 1,
            0.5f, -0.5f, 0.5f, 0, 1, 1,
            0.5f, 0.5f, 0.5f, 1, 1, 1,
            -0.5f, 0.5f, 0.5f, 0, 0, 0,
        };

        ushort[] indices =
        {
            0, 1, 2, 2, 3, 0, // back
            4, 5, 6, 6, 7, 4, // front
            0, 4, 7, 7, 3, 0, // left
            1, 5, 6, 6, 2, 1, // right
            3, 2, 6, 6, 7, 3, // top
            0, 1, 5, 5, 4, 0 // bottom
        };

        _vertexCount = indices.Length;

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(vertices.Length * sizeof(float)), BufferUsage.VertexBuffer));
        gd.UpdateBuffer(_vertexBuffer, 0, vertices);

        _indexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(indices.Length * sizeof(ushort)), BufferUsage.IndexBuffer));
        gd.UpdateBuffer(_indexBuffer, 0, indices);

        // Camera uniform
        _cameraBuffer = new CameraUniformBuffer(gd);

        // Vertex layout
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Color", VertexElementSemantic.Color, VertexElementFormat.Float3)
        );

        // Pipeline with camera buffer
        _pipeline = PipelineFactory.CreatePipeline(
            gd,
            "Graphics/Shaders/Mesh.vert",
            "Graphics/Shaders/Mesh.frag",
            vertexLayout,
            gd.MainSwapchain.Framebuffer,
            enableDepth: true,
            enableBlend: false,
            extraLayouts: [_cameraBuffer.Layout]
        );
    }

    public void Render(GraphicsDevice gd, CommandList cl, Camera? camera, Vector2 screenSize, int debugMode)
    {
        if (camera == null)
            return;
        
        _cameraBuffer.Update(gd, camera!, screenSize, debugMode);
        cl.Begin();
        cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
        cl.ClearColorTarget(0, RgbaFloat.CornflowerBlue);
        cl.ClearDepthStencil(1.0f);
        
        cl.SetPipeline(_pipeline);
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
        cl.DrawIndexed((uint)_vertexCount, 1, 0, 0, 0);
        cl.End();
        gd.SubmitCommands(cl);
    }
}
