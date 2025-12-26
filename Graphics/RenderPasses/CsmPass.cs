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
using Whisperleaf.Graphics.Shadows;

namespace Whisperleaf.Graphics.RenderPasses;

public class CsmPass : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;

    private readonly Pipeline _mdiPipeline;
    private readonly Pipeline _alphaPipeline;
    private readonly Pipeline _cullPipeline;

    private readonly DeviceBuffer _viewProjsBuffer; // StructuredBuffer of mat4
    private readonly DeviceBuffer _cullParamsBuffer; // Uniform
    private readonly DeviceBuffer _drawCameraBuffer; // Uniform for Graphics
    
    private DeviceBuffer? _batchBuffer;
    private DeviceBuffer? _instanceBatchMapBuffer;
    private DeviceBuffer? _indirectCommandsBuffer;
    private DeviceBuffer? _indirectCommandsStorageBuffer;
    private DeviceBuffer? _visibleInstancesBuffer;

    private ResourceSet? _viewProjsResourceSet;
    private ResourceSet? _cullParamsResourceSet;
    private ResourceSet? _drawCameraResourceSet;
    private ResourceSet? _cullResourceSet;
    private ResourceSet? _commandsResourceSet;
    private ResourceSet? _visibleResourceSet;
    
    private readonly ResourceLayout _viewProjsLayout;
    private readonly ResourceLayout _cullParamsLayout;
    private readonly ResourceLayout _vpLayout;
    private readonly ResourceLayout _modelLayout;
    private readonly ResourceLayout _batchLayout;
    private readonly ResourceLayout _commandsLayout;
    private readonly ResourceLayout _visibleInstancesLayout;

    private readonly List<BatchInfo> _opaqueBatches = new();
    private readonly List<uint> _instanceBatchMap = new();
    private readonly List<ShadowPass.AlphaTestInstance> _alphaInstances = new();
    private int _totalOpaqueInstances;
    private int _lastSceneVersion = -1;

    private struct CullParams {
        public uint TotalInstances;
        public uint NumBatches;
        private uint _pad0;
        private uint _pad1;
    }

    public CsmPass(GraphicsDevice gd)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;

        // 1. Layouts
        _viewProjsLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ShadowFaces", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute)));
        
        _cullParamsLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CullParams", ResourceKind.UniformBuffer, ShaderStages.Compute)));

        _vpLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Compute)));

        _modelLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ModelTransforms", ResourceKind.StructuredBufferReadWrite, ShaderStages.Vertex | ShaderStages.Compute)));

        _batchLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BatchData", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("InstanceBatchMap", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute)));

        _commandsLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("DrawCommands", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute | ShaderStages.Vertex)));

        _visibleInstancesLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("VisibleInstances", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute | ShaderStages.Vertex)));

        // 2. Buffers
        _viewProjsBuffer = _factory.CreateBuffer(new BufferDescription(64 * CsmAtlas.CascadeCount, BufferUsage.StructuredBufferReadOnly, 64));
        _viewProjsResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(_viewProjsLayout, _viewProjsBuffer));

        _cullParamsBuffer = _factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        _cullParamsResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(_cullParamsLayout, _cullParamsBuffer));

        _drawCameraBuffer = _factory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        _drawCameraResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(_vpLayout, _drawCameraBuffer));

        // 3. Shaders
        var cullShader = ShaderCache.GetShader(_gd, ShaderStages.Compute, "Graphics/Shaders/CullShadow.comp");
        var mdiVs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/ShadowMdi.vert");
        var shadowFs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/Shadow.frag");
        var alphaVs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/ShadowAlpha.vert");
        var alphaFs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/ShadowAlpha.frag");

        // 4. Vertex Layouts
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

        // 5. Pipelines
        var depthFormat = PixelFormat.D32_Float_S8_UInt;

        _cullPipeline = _factory.CreateComputePipeline(new ComputePipelineDescription(
            cullShader,
            new[] { _viewProjsLayout, _modelLayout, _batchLayout, _commandsLayout, _visibleInstancesLayout, _cullParamsLayout },
            64, 1, 1));

        _mdiPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.Front, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vertexLayout }, new[] { mdiVs, shadowFs }),
            new[] { _vpLayout, _modelLayout, _visibleInstancesLayout },
            new OutputDescription(new OutputAttachmentDescription(depthFormat))));

        _alphaPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { alphaVertexLayout }, new[] { alphaVs, alphaFs }),
            new[] { _vpLayout, _modelLayout, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout },
            new OutputDescription(new OutputAttachmentDescription(depthFormat))));
    }

    public void RefreshSceneBatches(GltfPass scene)
    {
        if (_lastSceneVersion == scene.StructureVersion) return;
        _lastSceneVersion = scene.StructureVersion;

        _opaqueBatches.Clear();
        _instanceBatchMap.Clear();
        _alphaInstances.Clear();
        _totalOpaqueInstances = 0;

        for (int i = 0; i < scene.MeshInstances.Count; i++)
        {
            var inst = scene.MeshInstances[i];
            var mat = scene.GetMaterial(inst.MaterialIndex);

            if (mat != null && (mat.AlphaMode == AlphaMode.Mask || mat.AlphaMode == AlphaMode.Blend))
            {
                _alphaInstances.Add(new ShadowPass.AlphaTestInstance {
                    Mesh = inst.Mesh,
                    Material = mat,
                    TransformIndex = inst.TransformIndex
                });
            }
            else
            {
                if (_opaqueBatches.Count == 0 || _opaqueBatches[^1].MeshIndex != (uint)inst.Mesh.GetHashCode())
                {
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

    private void RecreateBuffers()
    {
        _batchBuffer?.Dispose();
        _instanceBatchMapBuffer?.Dispose();
        _indirectCommandsBuffer?.Dispose();
        _indirectCommandsStorageBuffer?.Dispose();
        _visibleInstancesBuffer?.Dispose();
        _cullResourceSet?.Dispose();
        _commandsResourceSet?.Dispose();
        _visibleResourceSet?.Dispose();

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
            (uint)(_opaqueBatches.Count * 20 * CsmAtlas.CascadeCount), 
            BufferUsage.StructuredBufferReadWrite, 20));

        _indirectCommandsBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_opaqueBatches.Count * 20 * CsmAtlas.CascadeCount), 
            BufferUsage.IndirectBuffer, 0));

        _visibleInstancesBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_totalOpaqueInstances * 4 * CsmAtlas.CascadeCount), 
            BufferUsage.StructuredBufferReadWrite, 4));

        _cullResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _batchLayout, _batchBuffer, _instanceBatchMapBuffer));

        _commandsResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _commandsLayout, _indirectCommandsStorageBuffer));

        _visibleResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _visibleInstancesLayout, _visibleInstancesBuffer));
    }

    public void Render(CommandList cl, CsmAtlas atlas, GltfPass scene)
    {
        if (scene.MeshInstances.Count == 0) return;

        RefreshSceneBatches(scene);

        if (_totalOpaqueInstances == 0 && _alphaInstances.Count == 0) return;

        // --- PHASE 1: UNIFIED COMPUTE CULLING ---
        var matrices = new Matrix4x4[CsmAtlas.CascadeCount];
        for (int i = 0; i < CsmAtlas.CascadeCount; i++) matrices[i] = atlas.GetViewProj(i);
        cl.UpdateBuffer(_viewProjsBuffer, 0, matrices);

        var initialCommands = new IndirectDrawIndexedArguments[_opaqueBatches.Count * CsmAtlas.CascadeCount];
        for (int f = 0; f < CsmAtlas.CascadeCount; f++)
        {
            for (int b = 0; b < _opaqueBatches.Count; b++)
            {
                int idx = f * _opaqueBatches.Count + b;
                var batch = _opaqueBatches[b];
                initialCommands[idx] = new IndirectDrawIndexedArguments {
                    IndexCount = scene.MeshInstances[(int)batch.InstanceStart].Mesh.Range.IndexCount,
                    InstanceCount = 0,
                    FirstIndex = scene.MeshInstances[(int)batch.InstanceStart].Mesh.Range.IndexStart,
                    VertexOffset = scene.MeshInstances[(int)batch.InstanceStart].Mesh.Range.VertexOffset,
                    FirstInstance = (uint)(f * _totalOpaqueInstances) + batch.VisibleOffset
                };
            }
        }
        cl.UpdateBuffer(_indirectCommandsStorageBuffer, 0, initialCommands);

        var paramsData = new CullParams {
            TotalInstances = (uint)_totalOpaqueInstances,
            NumBatches = (uint)_opaqueBatches.Count
        };
        cl.UpdateBuffer(_cullParamsBuffer, 0, ref paramsData);

        cl.SetPipeline(_cullPipeline);
        cl.SetComputeResourceSet(0, _viewProjsResourceSet);
        cl.SetComputeResourceSet(1, scene.ModelBuffer.ResourceSet);
        cl.SetComputeResourceSet(2, _cullResourceSet);
        cl.SetComputeResourceSet(3, _commandsResourceSet);
        cl.SetComputeResourceSet(4, _visibleResourceSet);
        cl.SetComputeResourceSet(5, _cullParamsResourceSet);
        
        cl.Dispatch((uint)(_totalOpaqueInstances + 63) / 64, (uint)CsmAtlas.CascadeCount, 1);

        // --- PHASE 2: DRAWING ---
        cl.CopyBuffer(_indirectCommandsStorageBuffer, 0, _indirectCommandsBuffer, 0, _indirectCommandsStorageBuffer.SizeInBytes);

        for (int i = 0; i < CsmAtlas.CascadeCount; i++)
        {
            RenderCascade(cl, atlas, scene, i);
        }
    }

    private void RenderCascade(CommandList cl, CsmAtlas atlas, GltfPass scene, int cascadeIdx)
    {
        var fb = atlas.GetFramebuffer(cascadeIdx);
        cl.SetFramebuffer(fb);
        cl.ClearDepthStencil(1.0f); // CSM needs per-cascade clear

        var camData = new CameraUniform {
            View = Matrix4x4.Identity,
            Proj = Matrix4x4.Identity,
            ViewProjection = atlas.GetViewProj(cascadeIdx),
            CameraPos = Vector3.Zero
        };
        cl.UpdateBuffer(_drawCameraBuffer, 0, ref camData);

        // 1. Opaque Path (MDI)
        if (_totalOpaqueInstances > 0 && _indirectCommandsBuffer != null)
        {
            cl.SetPipeline(_mdiPipeline);
            cl.SetGraphicsResourceSet(0, _drawCameraResourceSet);
            cl.SetGraphicsResourceSet(1, scene.ModelBuffer.ResourceSet);
            cl.SetGraphicsResourceSet(2, _visibleResourceSet);

            cl.SetVertexBuffer(0, scene.GeometryBuffer.VertexBuffer);
            cl.SetIndexBuffer(scene.GeometryBuffer.IndexBuffer, IndexFormat.UInt32);

            uint cmdOffsetInBytes = (uint)(cascadeIdx * _opaqueBatches.Count * 20);
            cl.DrawIndexedIndirect(_indirectCommandsBuffer, cmdOffsetInBytes, (uint)_opaqueBatches.Count, 20);
        }

        // 2. Alpha Path (Direct)
        if (_alphaInstances.Count > 0)
        {
            cl.SetPipeline(_alphaPipeline);
            cl.SetGraphicsResourceSet(0, _drawCameraResourceSet);
            cl.SetGraphicsResourceSet(1, scene.ModelBuffer.ResourceSet);
            cl.SetVertexBuffer(0, scene.GeometryBuffer.VertexBuffer);
            cl.SetIndexBuffer(scene.GeometryBuffer.IndexBuffer, IndexFormat.UInt32);

            foreach (var inst in _alphaInstances)
            {
                if (inst.Material.ResourceSet != null && inst.Material.ParamsResourceSet != null)
                {
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

    public void Dispose()
    {
        _mdiPipeline.Dispose();
        _alphaPipeline.Dispose();
        _cullPipeline.Dispose();
        _viewProjsBuffer?.Dispose();
        _viewProjsResourceSet?.Dispose();
        _cullParamsBuffer?.Dispose();
        _cullParamsResourceSet?.Dispose();
        _drawCameraBuffer?.Dispose();
        _drawCameraResourceSet?.Dispose();
        _batchBuffer?.Dispose();
        _instanceBatchMapBuffer?.Dispose();
        _indirectCommandsBuffer?.Dispose();
        _indirectCommandsStorageBuffer?.Dispose();
        _visibleInstancesBuffer?.Dispose();
        _cullResourceSet?.Dispose();
        _commandsResourceSet?.Dispose();
        _visibleResourceSet?.Dispose();
        _viewProjsLayout.Dispose();
        _cullParamsLayout.Dispose();
        _vpLayout.Dispose();
        _modelLayout.Dispose();
        _batchLayout.Dispose();
        _commandsLayout.Dispose();
        _visibleInstancesLayout.Dispose();
    }
}
