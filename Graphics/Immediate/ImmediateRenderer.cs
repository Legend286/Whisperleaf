using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.Immediate;

[StructLayout(LayoutKind.Sequential)]
public struct VertexPositionColor
{
    public Vector3 Position;
    public RgbaFloat Color;
    public VertexPositionColor(Vector3 pos, RgbaFloat color) { Position = pos; Color = color; }
    public const uint SizeInBytes = 28; // 12 + 16
}

public class ImmediateRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Pipeline _pipeline;
    private DeviceBuffer _vertexBuffer;
    
    private readonly List<VertexPositionColor> _vertices = new();
    private readonly CameraUniformBuffer _cameraBuffer;
    
    public ImmediateRenderer(GraphicsDevice gd, OutputDescription outputDescription)
    {
        _gd = gd;
        _cameraBuffer = new CameraUniformBuffer(gd);

        var factory = gd.ResourceFactory;

        // Vertex Layout
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
        );

        // Shaders
        var shaders = ShaderCache.GetShaderPair(gd, "Graphics/Shaders/Immediate.vert", "Graphics/Shaders/Immediate.frag");

        // Pipeline
        var pd = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.LineList,
            ResourceLayouts = new[] { _cameraBuffer.Layout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, shaders),
            Outputs = outputDescription
        };

        _pipeline = factory.CreateGraphicsPipeline(pd);

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(1024 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
    }

    public void DrawLine(Vector3 p1, Vector3 p2, RgbaFloat color)
    {
        _vertices.Add(new VertexPositionColor(p1, color));
        _vertices.Add(new VertexPositionColor(p2, color));
    }

    public void DrawAABB(Vector3 min, Vector3 max, RgbaFloat color)
    {
        // 12 lines
        var p0 = new Vector3(min.X, min.Y, min.Z);
        var p1 = new Vector3(max.X, min.Y, min.Z);
        var p2 = new Vector3(max.X, max.Y, min.Z);
        var p3 = new Vector3(min.X, max.Y, min.Z);
        var p4 = new Vector3(min.X, min.Y, max.Z);
        var p5 = new Vector3(max.X, min.Y, max.Z);
        var p6 = new Vector3(max.X, max.Y, max.Z);
        var p7 = new Vector3(min.X, max.Y, max.Z);

        DrawLine(p0, p1, color); DrawLine(p1, p2, color); DrawLine(p2, p3, color); DrawLine(p3, p0, color);
        DrawLine(p4, p5, color); DrawLine(p5, p6, color); DrawLine(p6, p7, color); DrawLine(p7, p4, color);
        DrawLine(p0, p4, color); DrawLine(p1, p5, color); DrawLine(p2, p6, color); DrawLine(p3, p7, color);
    }

    public void Render(CommandList cl, Camera camera, Vector2 screenSize, int debugMode = 0)
    {
        if (_vertices.Count == 0) return;

        _cameraBuffer.Update(_gd, camera, screenSize, debugMode);

        EnsureBufferSize((uint)_vertices.Count);
        _gd.UpdateBuffer(_vertexBuffer, 0, _vertices.ToArray());

        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.Draw((uint)_vertices.Count);

        _vertices.Clear();
    }

    private void EnsureBufferSize(uint elementCount)
    {
        uint neededSize = elementCount * VertexPositionColor.SizeInBytes;
        if (_vertexBuffer.SizeInBytes < neededSize)
        {
            _vertexBuffer.Dispose();
            uint newSize = Math.Max(neededSize, (uint)(_vertexBuffer.SizeInBytes * 2));
            _vertexBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(newSize, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }
    }

    public void Dispose()
    {
        _cameraBuffer.Dispose();
        _pipeline.Dispose();
        _vertexBuffer.Dispose();
    }
}
