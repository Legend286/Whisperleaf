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

    private readonly Pipeline _shadowPipeline;
    private readonly Pipeline _alphaPipeline;

    private readonly CameraUniformBuffer[] _cascadeCameras;
    
    // We need to keep the layout alive if we created it.
    private ResourceLayout _localModelLayout;

    public CsmPass(GraphicsDevice gd)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;

        // 1. Buffers (Cameras)
        _cascadeCameras = new CameraUniformBuffer[CsmAtlas.CascadeCount];
        for (int i = 0; i < CsmAtlas.CascadeCount; i++)
        {
            _cascadeCameras[i] = new CameraUniformBuffer(gd);
        }

        // 2. Shaders
        var shadowVs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/Shadow.vert");
        var shadowFs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/Shadow.frag");
        var alphaVs = ShaderCache.GetShader(_gd, ShaderStages.Vertex, "Graphics/Shaders/ShadowAlpha.vert");
        var alphaFs = ShaderCache.GetShader(_gd, ShaderStages.Fragment, "Graphics/Shaders/ShadowAlpha.frag");

        // 3. Vertex Layouts
        var vertexLayout = new VertexLayoutDescription(
            48,
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4),
            new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
        );

        // 4. Pipelines
        var depthFormat = PixelFormat.D32_Float_S8_UInt;
        
        // We can create a compatible layout here.
        var compatibleModelLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ModelTransforms", ResourceKind.StructuredBufferReadWrite, ShaderStages.Vertex | ShaderStages.Compute)));
        _localModelLayout = compatibleModelLayout;

        // Use layout from first camera (all are same)
        var cameraLayout = _cascadeCameras[0].Layout;

        _shadowPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.Front, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vertexLayout }, new[] { shadowVs, shadowFs }),
            new[] { cameraLayout, compatibleModelLayout }, 
            new OutputDescription(new OutputAttachmentDescription(depthFormat))));

        _alphaPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vertexLayout }, new[] { alphaVs, alphaFs }),
            new[] { cameraLayout, compatibleModelLayout, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout },
            new OutputDescription(new OutputAttachmentDescription(depthFormat))));
    }
    
    public void Update(GltfPass scene)
    {
        // No heavy update
    }

    public void PrepareRender(CsmAtlas atlas, GltfPass scene)
    {
        for (int i = 0; i < CsmAtlas.CascadeCount; i++)
        {
            var camData = new CameraUniform {
                View = Matrix4x4.Identity,
                Proj = Matrix4x4.Identity,
                ViewProjection = atlas.GetViewProj(i),
                CameraPos = Vector3.Zero
            };
            _cascadeCameras[i].Update(_gd, ref camData);
        }
    }

    public void Render(CommandList cl, CsmAtlas atlas, GltfPass scene)
    {
        if (scene.MeshInstances.Count == 0) return;

        for (int i = 0; i < CsmAtlas.CascadeCount; i++)
        {
            RenderCascade(cl, atlas, scene, i);
        }
    }

    private void RenderCascade(CommandList cl, CsmAtlas atlas, GltfPass scene, int cascadeIdx)
    {
        var fb = atlas.GetFramebuffer(cascadeIdx);
        cl.SetFramebuffer(fb);
        cl.ClearDepthStencil(1.0f);

        cl.SetVertexBuffer(0, scene.GeometryBuffer.VertexBuffer);
        cl.SetIndexBuffer(scene.GeometryBuffer.IndexBuffer, IndexFormat.UInt32);

        // Cull instances against cascade frustum
        var frustum = new Frustum(atlas.GetViewProj(cascadeIdx));
        var (visibleIndices, _) = scene.Query(frustum);
        visibleIndices.Sort();

        // 1. Opaque Pass
        cl.SetPipeline(_shadowPipeline);
        cl.SetGraphicsResourceSet(0, _cascadeCameras[cascadeIdx].ResourceSet);
        cl.SetGraphicsResourceSet(1, scene.ModelBuffer.ResourceSet);
        
        DrawBatches(cl, scene, visibleIndices, false);

        // 2. Alpha Pass
        cl.SetPipeline(_alphaPipeline);
        cl.SetGraphicsResourceSet(0, _cascadeCameras[cascadeIdx].ResourceSet);
        cl.SetGraphicsResourceSet(1, scene.ModelBuffer.ResourceSet);

        DrawBatches(cl, scene, visibleIndices, true);
    }
    
    private void DrawBatches(CommandList cl, GltfPass scene, List<int> visibleIndices, bool alphaPass)
    {
        int listIndex = 0;
        while (listIndex < visibleIndices.Count)
        {
            int instanceIdx = visibleIndices[listIndex];
            
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
            
            var batchMesh = inst.Mesh;
            int batchMaterialIndex = inst.MaterialIndex;
            int startInstanceIdx = instanceIdx;
            int instanceCount = 0;
            
            while (listIndex < visibleIndices.Count) {
                int nextIdx = visibleIndices[listIndex];
                if (nextIdx >= scene.MeshInstances.Count) break;
                
                var nextInst = scene.MeshInstances[nextIdx];
                if (nextInst.Mesh != batchMesh || nextInst.MaterialIndex != batchMaterialIndex) break;
                
                // Ensure continuity for instancing
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
                    (uint)startInstanceIdx
                );
            }
        }
    }

    public void Dispose()
    {
        _shadowPipeline.Dispose();
        _alphaPipeline.Dispose();
        
        foreach (var c in _cascadeCameras) c.Dispose();
        
        _localModelLayout?.Dispose();
    }
}
