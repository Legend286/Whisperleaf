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

public class ShadowPass : IDisposable {
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;

    // Pipelines
    private readonly Pipeline _shadowPipeline;
    private readonly Pipeline _alphaPipeline;

    // Buffers
    private DeviceBuffer[] _drawCameraBuffers; // Uniform for Graphics (Per Face)
    private ResourceSet[] _drawCameraResourceSets;
    private ResourceLayout _vpLayout; 

    // Scene Data
    private int _lastSceneVersion = -1;

    private const int MaxFaces = 1024; 

    [StructLayout(LayoutKind.Sequential)]
    private struct CullParams {
        public uint TotalInstances;
        public uint NumBatches;
        private Vector2 _padding;
    }

    public struct AlphaTestInstance {
        public MeshGpu Mesh;
        public MaterialData Material;
        public int TransformIndex;
    }

    public ShadowPass(GraphicsDevice gd, ResourceLayout modelLayout) {
        _gd = gd;
        _factory = gd.ResourceFactory;

        _vpLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        
        // 2. Buffers
        _drawCameraBuffers = new DeviceBuffer[MaxFaces];
        _drawCameraResourceSets = new ResourceSet[MaxFaces];
        for (int i = 0; i < MaxFaces; i++)
        {
            _drawCameraBuffers[i] = _factory.CreateBuffer(new BufferDescription(224, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _drawCameraResourceSets[i] = _factory.CreateResourceSet(new ResourceSetDescription(_vpLayout, _drawCameraBuffers[i]));
        }

        // 3. Shaders
        var shadowVs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/Shadow.vert");
        var shadowFs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/Shadow.frag");
        var alphaVs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/ShadowAlpha.vert");
        var alphaFs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/ShadowAlpha.frag");

        // 4. Vertex Layouts
        var vertexLayout = new VertexLayoutDescription(
            48,
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4),
            new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
        );

        // 5. Pipelines
        var depthFormat = PixelFormat.D32_Float_S8_UInt;

        // Opaque Pipeline (Standard Instancing)
        _shadowPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.Front, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vertexLayout }, new[] { shadowVs, shadowFs }),
            new[] { _vpLayout, modelLayout }, 
            new OutputDescription(new OutputAttachmentDescription(depthFormat))));

        // Alpha Pipeline
        _alphaPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vertexLayout }, new[] { alphaVs, alphaFs }),
            new[] { _vpLayout, modelLayout, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout },
            new OutputDescription(new OutputAttachmentDescription(depthFormat))));
    }

    public void Update(GltfPass scene)
    {
        // No heavy update needed anymore since we just iterate instances
    }

    public void Render(GraphicsDevice gd, CommandList cl, ShadowAtlas atlas, GltfPass scene) {
        if (scene.MeshInstances.Count == 0) return;

        var allocatedNodes = new List<SceneNode>(atlas.GetAllocatedNodes());
        if (allocatedNodes.Count == 0) return;

        int faceIdx = 0;
        foreach (var node in allocatedNodes) {
            var allocs = atlas.GetAllocations(node);
            if (allocs == null) continue;

            foreach (var alloc in allocs) {
                if (faceIdx >= MaxFaces) break;
                RenderShadowMap(cl, atlas, scene, alloc, faceIdx);
                faceIdx++;
            }
        }
    }

    private void RenderShadowMap(CommandList cl, ShadowAtlas atlas, GltfPass scene, ShadowAtlas.ShadowAllocation alloc, int faceIdx) {
        var camData = new CameraUniform {
            View = Matrix4x4.Identity,
            Proj = Matrix4x4.Identity,
            ViewProjection = alloc.ViewProj,
            CameraPos = Vector3.Zero
        };
        cl.UpdateBuffer(_drawCameraBuffers[faceIdx], 0, ref camData);

        var fb = atlas.GetFramebuffer(alloc.PageIndex);
        cl.SetFramebuffer(fb);
        cl.SetViewport(0, new Viewport(alloc.AtlasX * alloc.TileSize, alloc.AtlasY * alloc.TileSize, alloc.TileSize, alloc.TileSize, 0, 1));
        
        cl.SetVertexBuffer(0, scene.GeometryBuffer.VertexBuffer);
        cl.SetIndexBuffer(scene.GeometryBuffer.IndexBuffer, IndexFormat.UInt32);

        // Cull instances against this shadow face frustum
        var frustum = new Frustum(alloc.ViewProj);
        // Note: Ideally use BVH here, but for simplicity/robustness as requested, we iterate.
        // Or better, query the scene BVH.
        var (visibleIndices, stats) = scene.Query(frustum);
        visibleIndices.Sort();

        // Separate opaque and alpha batches
        // Since GltfPass sorts by material/mesh, we can batch easily.
        
        // 1. Opaque Pass
        cl.SetPipeline(_shadowPipeline);
        cl.SetGraphicsResourceSet(0, _drawCameraResourceSets[faceIdx]);
        cl.SetGraphicsResourceSet(1, scene.ModelBuffer.ResourceSet); // Bind ALL models
        
        DrawBatches(cl, scene, visibleIndices, false);

        // 2. Alpha Pass
        cl.SetPipeline(_alphaPipeline);
        cl.SetGraphicsResourceSet(0, _drawCameraResourceSets[faceIdx]);
        cl.SetGraphicsResourceSet(1, scene.ModelBuffer.ResourceSet);

        DrawBatches(cl, scene, visibleIndices, true);
    }
    
    private void DrawBatches(CommandList cl, GltfPass scene, List<int> visibleIndices, bool alphaPass)
    {
        int listIndex = 0;
        while (listIndex < visibleIndices.Count)
        {
            int instanceIdx = visibleIndices[listIndex];
            
            // Skip lights
            if (instanceIdx >= scene.MeshInstances.Count) {
                listIndex++;
                continue;
            }
            
            var inst = scene.MeshInstances[instanceIdx];
            var mat = scene.GetMaterial(inst.MaterialIndex);
            bool isAlpha = mat != null && (mat.AlphaMode == AlphaMode.Mask || mat.AlphaMode == AlphaMode.Blend);
            
            if (isAlpha != alphaPass) {
                listIndex++;
                continue;
            }
            
            // Start batch
            var batchMesh = inst.Mesh;
            int batchMaterialIndex = inst.MaterialIndex;
            int startInstanceIdx = instanceIdx;
            int instanceCount = 0;
            
            // Accumulate consecutive instances of same mesh/mat
            // Note: Since we are iterating a sorted list of visible indices, 
            // but the original indices are sorted by mesh/mat, 
            // consecutive visible indices *might* not be consecutive in the original list.
            // BUT: DrawIndexed(..., firstInstance) requires a block of transforms in the buffer?
            // Actually, we use gl_InstanceIndex to index into u_Models.
            // If we use standard DrawIndexed(..., firstInstance), then u_Models[firstInstance + i] is accessed.
            // This requires the instances to be contiguous in the model buffer.
            // GltfPass sorts instances, so they ARE contiguous in buffer.
            // But 'visibleIndices' might skip some.
            // So we can't batch efficiently with simple instancing unless we repack the buffer (MDI approach) 
            // OR if we draw one by one.
            
            // "make it draw using regular instanced drawcalls same path as alphatest"
            // The alpha path usually draws one by one because materials differ.
            // If we revert to drawing one by one (or batching identical *contiguous* visible instances), it's safer.
            
            // Check if next visible instance is exactly next in buffer (contiguous range)
            while (listIndex < visibleIndices.Count) {
                int nextIdx = visibleIndices[listIndex];
                if (nextIdx >= scene.MeshInstances.Count) break;
                
                var nextInst = scene.MeshInstances[nextIdx];
                if (nextInst.Mesh != batchMesh || nextInst.MaterialIndex != batchMaterialIndex) break; // Mesh/Mat changed
                
                // For valid instancing with 'firstInstance', indices must be sequential: nextIdx == prevIdx + 1
                // If there's a gap (culled instance), we must break batch or use a remapped buffer (which we don't have here).
                // So: check continuity.
                if (instanceCount > 0 && nextIdx != (startInstanceIdx + instanceCount)) break;

                instanceCount++;
                listIndex++;
            }
            
            if (instanceCount > 0) {
                if (alphaPass && mat != null) {
                    cl.SetGraphicsResourceSet(2, mat.ResourceSet);
                    cl.SetGraphicsResourceSet(3, mat.ParamsResourceSet);
                }
                
                cl.DrawIndexed(
                    batchMesh.Range.IndexCount,
                    (uint)instanceCount,
                    batchMesh.Range.IndexStart,
                    batchMesh.Range.VertexOffset,
                    (uint)startInstanceIdx // Maps to u_Models[startInstanceIdx]
                );
            }
        }
    }

    public void Dispose() {
        _shadowPipeline.Dispose();
        _alphaPipeline.Dispose();
        foreach (var b in _drawCameraBuffers) b?.Dispose();
        foreach (var s in _drawCameraResourceSets) s?.Dispose();
        _vpLayout.Dispose();
    }
}
