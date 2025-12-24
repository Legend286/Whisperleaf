using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.Assets;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.RenderPasses;

public class DepthPass : IDisposable {
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;

    private readonly Pipeline _mdiPipeline;
    private readonly Pipeline _alphaPipeline;
    private readonly Pipeline _cullPipeline;

    private readonly CameraUniformBuffer _cameraBuffer;
    private DeviceBuffer? _batchBuffer;
    private DeviceBuffer? _instanceBatchMapBuffer;
    private DeviceBuffer? _indirectCommandsBuffer;
    private DeviceBuffer? _indirectCommandsStorageBuffer;
    private DeviceBuffer? _visibleInstancesBuffer;

    private ResourceSet? _cullResourceSet;
    private ResourceSet? _commandsResourceSet;
    private ResourceSet? _visibleResourceSet;
    private readonly ResourceLayout _modelLayout;
    private readonly ResourceLayout _batchLayout;
    private readonly ResourceLayout _commandsLayout;
    private readonly ResourceLayout _visibleInstancesLayout;

    private readonly List<BatchInfo> _opaqueBatches = new();
    private readonly List<uint> _instanceBatchMap = new();
    private readonly List<ShadowPass.AlphaTestInstance> _alphaInstances = new();
    private int _totalOpaqueInstances;
    private int _lastSceneVersion = -1;

    public DepthPass(GraphicsDevice gd, Framebuffer target, CameraUniformBuffer cameraBuffer) {
        _gd = gd;
        _factory = gd.ResourceFactory;
        _cameraBuffer = cameraBuffer;

        var vpLayout = cameraBuffer.Layout;
        
        _modelLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ModelTransforms", ResourceKind.StructuredBufferReadWrite, ShaderStages.Vertex | ShaderStages.Compute)));

        _batchLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BatchData", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("InstanceBatchMap", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute)));

        _commandsLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("DrawCommands", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute | ShaderStages.Vertex)));

        _visibleInstancesLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("VisibleInstances", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute | ShaderStages.Vertex)));

        var cullShader = ShaderCache.GetShader(_gd, ShaderStages.Compute, "Graphics/Shaders/Cull.comp");
        var mdiVs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/ShadowMdi.vert");
        var depthFs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/Shadow.frag");
        var alphaVs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/ShadowAlpha.vert");
        var alphaFs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/DepthAlpha.frag");

        var vertexLayout = new VertexLayoutDescription(
            48,
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
        );

        var alphaVertexLayout = new VertexLayoutDescription(
            48,
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3, 12),
            new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4, 24),
            new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 40)
        );

        _cullPipeline = _factory.CreateComputePipeline(new ComputePipelineDescription(
            cullShader,
            new[] { vpLayout, _modelLayout, _batchLayout, _commandsLayout, _visibleInstancesLayout },
            64, 1, 1));

        _mdiPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            new RasterizerStateDescription(FaceCullMode.Front, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vertexLayout }, new[] { mdiVs, depthFs }),
            new[] { vpLayout, _modelLayout, _visibleInstancesLayout },
            target.OutputDescription));

        _alphaPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { alphaVertexLayout }, new[] { alphaVs, alphaFs }),
            new[] { vpLayout, _modelLayout, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout },
            target.OutputDescription));
    }

    public void RefreshSceneBatches(GltfPass scene) {
        if (_lastSceneVersion == scene.StructureVersion) return;
        _lastSceneVersion = scene.StructureVersion;

        _opaqueBatches.Clear();
        _instanceBatchMap.Clear();
        _alphaInstances.Clear();
        _totalOpaqueInstances = 0;

        for (int i = 0; i < scene.MeshInstances.Count; i++) {
            var inst = scene.MeshInstances[i];
            var mat = scene.GetMaterial(inst.MaterialIndex);

            bool isAlpha = mat != null && (mat.AlphaMode == AlphaMode.Mask || mat.AlphaMode == AlphaMode.Blend);

            if (isAlpha) {
                _alphaInstances.Add(new ShadowPass.AlphaTestInstance {
                    Mesh = inst.Mesh,
                    Material = mat,
                    TransformIndex = inst.TransformIndex
                });
            } else {
                if (_opaqueBatches.Count == 0 || _opaqueBatches[^1].MeshIndex != (uint)inst.Mesh.GetHashCode()) {
                    _opaqueBatches.Add(new BatchInfo(
                        (uint)i,
                        0,
                        (uint)i,
                        (uint)inst.Mesh.GetHashCode(),
                        inst.Mesh.AABBMin,
                        inst.Mesh.AABBMax));
                }

                var b = _opaqueBatches[^1];
                b.InstanceCount++;
                _opaqueBatches[^1] = b;

                _instanceBatchMap.Add((uint)_opaqueBatches.Count - 1);
                _totalOpaqueInstances++;
            }
        }

        RecreateBuffers();
    }

    private void RecreateBuffers() {
        _batchBuffer?.Dispose();
        _instanceBatchMapBuffer?.Dispose();
        _indirectCommandsBuffer?.Dispose();
        _indirectCommandsStorageBuffer?.Dispose();
        _visibleInstancesBuffer?.Dispose();
        _cullResourceSet?.Dispose();

        if (_opaqueBatches.Count == 0) return;

        _batchBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_opaqueBatches.Count * Marshal.SizeOf<BatchInfo>()), 
            BufferUsage.StructuredBufferReadWrite, 
            (uint)Marshal.SizeOf<BatchInfo>()));
        _gd.UpdateBuffer(_batchBuffer, 0, _opaqueBatches.ToArray());

        _instanceBatchMapBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_instanceBatchMap.Count * 4), 
            BufferUsage.StructuredBufferReadWrite, 
            4));
        _gd.UpdateBuffer(_instanceBatchMapBuffer, 0, _instanceBatchMap.ToArray());

        _indirectCommandsStorageBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_opaqueBatches.Count * 20), 
            BufferUsage.StructuredBufferReadWrite, 
            20));

        _indirectCommandsBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_opaqueBatches.Count * 20), 
            BufferUsage.IndirectBuffer));

        _visibleInstancesBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_totalOpaqueInstances * 4), 
            BufferUsage.StructuredBufferReadWrite, 
            4));

        _cullResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _batchLayout, _batchBuffer, _instanceBatchMapBuffer));

        _commandsResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _commandsLayout, _indirectCommandsStorageBuffer));

        _visibleResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _visibleInstancesLayout, _visibleInstancesBuffer));
    }

    public void Render(CommandList cl, GltfPass scene, Camera camera) {
        if (scene.MeshInstances.Count == 0) return;

        RefreshSceneBatches(scene);

        if (_totalOpaqueInstances == 0 && _alphaInstances.Count == 0) return;

        // Note: CameraBuffer is updated by GltfPass.PrepareRender

        if (_totalOpaqueInstances > 0 && _indirectCommandsBuffer != null) {
            var commands = new IndirectDrawIndexedArguments[_opaqueBatches.Count];
            for (int i = 0; i < _opaqueBatches.Count; i++) {
                var batch = _opaqueBatches[i];
                commands[i] = new IndirectDrawIndexedArguments {
                    IndexCount = scene.MeshInstances[(int)batch.InstanceStart].Mesh.Range.IndexCount,
                    InstanceCount = 0,
                    FirstIndex = scene.MeshInstances[(int)batch.InstanceStart].Mesh.Range.IndexStart,
                    VertexOffset = scene.MeshInstances[(int)batch.InstanceStart].Mesh.Range.VertexOffset,
                    FirstInstance = batch.VisibleOffset
                };
            }
            cl.UpdateBuffer(_indirectCommandsStorageBuffer, 0, commands);

            cl.SetPipeline(_cullPipeline);
            cl.SetComputeResourceSet(0, _cameraBuffer.ResourceSet);
            cl.SetComputeResourceSet(1, scene.ModelBuffer.ResourceSet);
            cl.SetComputeResourceSet(2, _cullResourceSet);
            
            cl.SetComputeResourceSet(3, _commandsResourceSet);
            cl.SetComputeResourceSet(4, _visibleResourceSet);

            cl.Dispatch((uint)(_totalOpaqueInstances + 63) / 64, 1, 1);
            
            // Copy computed commands to indirect buffer
            cl.CopyBuffer(_indirectCommandsStorageBuffer, 0, _indirectCommandsBuffer, 0, _indirectCommandsStorageBuffer.SizeInBytes);

            cl.SetPipeline(_mdiPipeline);
            cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
            cl.SetGraphicsResourceSet(1, scene.ModelBuffer.ResourceSet);
            cl.SetGraphicsResourceSet(2, _visibleResourceSet);

            cl.SetVertexBuffer(0, scene.GeometryBuffer.VertexBuffer);
            cl.SetIndexBuffer(scene.GeometryBuffer.IndexBuffer, IndexFormat.UInt32);

            cl.DrawIndexedIndirect(_indirectCommandsBuffer, 0, (uint)_opaqueBatches.Count, 20);
        }

        if (_alphaInstances.Count > 0) {
            cl.SetPipeline(_alphaPipeline);
            cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
            cl.SetGraphicsResourceSet(1, scene.ModelBuffer.ResourceSet);
            cl.SetVertexBuffer(0, scene.GeometryBuffer.VertexBuffer);
            cl.SetIndexBuffer(scene.GeometryBuffer.IndexBuffer, IndexFormat.UInt32);

            foreach (var inst in _alphaInstances) {
                if (inst.Material.ResourceSet != null && inst.Material.ParamsResourceSet != null) {
                    cl.SetGraphicsResourceSet(2, inst.Material.ResourceSet);
                    cl.SetGraphicsResourceSet(3, inst.Material.ParamsResourceSet);

                    cl.DrawIndexed(
                        inst.Mesh.Range.IndexCount,
                        1,
                        inst.Mesh.Range.IndexStart,
                        inst.Mesh.Range.VertexOffset,
                        (uint)inst.TransformIndex);
                }
            }
        }
    }

    public void Dispose() {
        _mdiPipeline.Dispose();
        _alphaPipeline.Dispose();
        _cullPipeline.Dispose();
        // Do NOT dispose _cameraBuffer (shared)
        _batchBuffer?.Dispose();
        _instanceBatchMapBuffer?.Dispose();
        _indirectCommandsBuffer?.Dispose();
        _indirectCommandsStorageBuffer?.Dispose();
        _visibleInstancesBuffer?.Dispose();
        _cullResourceSet?.Dispose();
        _commandsResourceSet?.Dispose();
        _visibleResourceSet?.Dispose();
        _modelLayout.Dispose();
        _batchLayout.Dispose();
        _commandsLayout.Dispose();
        _visibleInstancesLayout.Dispose();
    }
}