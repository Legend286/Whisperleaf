using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.Assets;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;
using Whisperleaf.Graphics.Shadows;

namespace Whisperleaf.Graphics.RenderPasses;

public class ShadowPass : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Pipeline _pipeline;
    private readonly DeviceBuffer _viewProjBuffer;
    private readonly ModelUniformBuffer _modelBuffer;
    private ResourceSet _viewProjResourceSet;
    
    // Reusable lists to avoid allocs
    private readonly List<int> _visibleIndices = new();
    private readonly List<ModelUniform> _shadowTransforms = new();
    
    public ShadowPass(GraphicsDevice gd)
    {
        _gd = gd;
        var factory = gd.ResourceFactory;

        _viewProjBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
        var vpLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ViewProj", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        _viewProjResourceSet = factory.CreateResourceSet(new ResourceSetDescription(vpLayout, _viewProjBuffer));

        _modelBuffer = new ModelUniformBuffer(gd);

        var vertexLayout = new VertexLayoutDescription(
            48, // Explicit stride to match GltfPass geometry buffer layout
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
        );

        var depthFormat = PixelFormat.D32_Float_S8_UInt;
        var pd = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerStateDescription.CullNone, // Render backfaces for shadows? Or Front? CullNone is safer for leaky shadows sometimes.
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(
                new[] { vertexLayout },
                ShaderCache.GetShaderPair(gd, "Graphics/Shaders/Shadow.vert", "Graphics/Shaders/Shadow.frag")
            ),
            new[] { vpLayout, _modelBuffer.Layout },
            new OutputDescription(new OutputAttachmentDescription(depthFormat))
        );
        _pipeline = factory.CreateGraphicsPipeline(pd);
    }

    public void Render(GraphicsDevice gd, CommandList cl, ShadowAtlas atlas, GltfPass scene)
    {
        foreach (var node in atlas.GetAllocatedNodes())
        {
            RenderLight(gd, cl, atlas, scene, node);
        }
    }

    public void RenderLight(GraphicsDevice gd, CommandList cl, ShadowAtlas atlas, GltfPass scene, SceneNode lightNode)
    {
        var allocs = atlas.GetAllocations(lightNode);
        if (allocs == null || allocs.Length == 0) return;

        if (!scene.TryGetWorldTransform(lightNode, out var lightWorld)) return;
        var lightPos = lightWorld.Translation;
        var lightDir = Vector3.TransformNormal(new Vector3(0, 0, -1), lightWorld);

        // Process each allocation (Face)
        foreach (var alloc in allocs)
        {
            // 1. Setup ViewProj
            Matrix4x4 view, proj;
            float near = 0.05f;
            float far = Math.Max(lightNode.Light.Range, near + 0.05f);
            
            if (lightNode.Light!.Type == 0) // Point
            {
                // Cube face
                view = GetPointLightView(lightPos, alloc.FaceIndex);
                proj = Matrix4x4.CreatePerspectiveFieldOfView((MathF.PI / 2.0f) + 0.05f, 1.0f, near, far);
            }
            else // Spot (or Directional?)
            {
                view = Matrix4x4.CreateLookAt(lightPos, lightPos + lightDir, Vector3.UnitY);
                // FOV = OuterCone * 2
                float fov = lightNode.Light.OuterCone * 2.0f;
                if (fov < 0.01f) fov = 0.01f;
                if (fov > MathF.PI - 0.1f) fov = MathF.PI - 0.1f;
                
                proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, 1.0f, near, far);
            }
            
            var viewProj = view * proj;
            
            // Update allocation with ViewProj for shader use later
            atlas.UpdateViewProj(lightNode, alloc.FaceIndex, viewProj);


            // 2. Cull
            // Use Frustum Culling from Root.
            // Previous optimization using FindEnclosingNode was incorrect for Object BVH (overlapping nodes).
            var frustum = new Frustum(viewProj);
            var (indices, stats) = scene.Query(frustum); // Call GltfPass.Query which queries both BVHs
            
            if (indices.Count == 0) continue;
            
            // 3. Prepare Batch
            _shadowTransforms.Clear();
            var drawRanges = new List<(MeshGpu Mesh, int StartIndex, int Count)>();
            
            indices.Sort();
            
            // Batching logic similar to GltfPass but ignoring materials
            int listIndex = 0;
            int currentStart = 0;
            
            while (listIndex < indices.Count)
            {
                int instanceIdx = indices[listIndex];
                var batchInst = scene.MeshInstances[instanceIdx];
                var batchMesh = batchInst.Mesh;
                int count = 0;
                
                while (listIndex < indices.Count)
                {
                    int nextIdx = indices[listIndex];
                    var nextInst = scene.MeshInstances[nextIdx];
                    
                    if (nextInst.Mesh != batchMesh) break;
                    
                    if (scene.TryGetWorldTransform(nextInst.Node, out var world))
                    {
                        _shadowTransforms.Add(new ModelUniform(world));
                        count++;
                    }
                    listIndex++;
                }
                
                if (count > 0)
                {
                    drawRanges.Add((batchMesh, currentStart, count));
                    currentStart += count;
                }
            }
            
            if (_shadowTransforms.Count == 0) continue;
            
            // 4. Upload & Draw
            cl.UpdateBuffer(_viewProjBuffer, 0, viewProj);
            _modelBuffer.UpdateAll(cl, CollectionsMarshal.AsSpan(_shadowTransforms));
            
            // Bind Framebuffer for this Layer
            var fb = atlas.GetFramebuffer(alloc.PageIndex);
            cl.SetFramebuffer(fb);
            
            // Set Viewport to the specific Tile
            cl.SetViewport(0, new Viewport(alloc.AtlasX * alloc.TileSize, alloc.AtlasY * alloc.TileSize, alloc.TileSize, alloc.TileSize, 0, 1));
            
            // Note: We are rendering to a specific Layer.
            // The Framebuffer provided by GetFramebuffer(layer) should be attached to that layer.
            
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _viewProjResourceSet);
            cl.SetGraphicsResourceSet(1, _modelBuffer.ResourceSet);
            
            foreach (var batch in drawRanges)
            {
                cl.SetVertexBuffer(0, batch.Mesh.VertexBuffer);
                cl.SetIndexBuffer(batch.Mesh.IndexBuffer, IndexFormat.UInt32);
                
                cl.DrawIndexed(
                    batch.Mesh.Range.IndexCount,
                    (uint)batch.Count,
                    batch.Mesh.Range.IndexStart,
                    batch.Mesh.Range.VertexOffset,
                    (uint)batch.StartIndex
                );
            }
        }
    }

    private Matrix4x4 GetPointLightView(Vector3 pos, int face)
    {
        // Standard Cube Map faces
        // 0: +X, 1: -X, 2: +Y, 3: -Y, 4: +Z, 5: -Z
        // Up vectors must be standard
        return face switch
        {
            0 => Matrix4x4.CreateLookAt(pos, pos + Vector3.UnitX, Vector3.UnitY),
            1 => Matrix4x4.CreateLookAt(pos, pos - Vector3.UnitX, Vector3.UnitY),
            2 => Matrix4x4.CreateLookAt(pos, pos + Vector3.UnitY, -Vector3.UnitZ), // Top check up
            3 => Matrix4x4.CreateLookAt(pos, pos - Vector3.UnitY, Vector3.UnitZ), // Bottom check up
            4 => Matrix4x4.CreateLookAt(pos, pos + Vector3.UnitZ, Vector3.UnitY),
            5 => Matrix4x4.CreateLookAt(pos, pos - Vector3.UnitZ, Vector3.UnitY),
            _ => Matrix4x4.Identity
        };
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _viewProjBuffer.Dispose();
        _viewProjResourceSet.Dispose();
        _modelBuffer.Dispose();
    }
}