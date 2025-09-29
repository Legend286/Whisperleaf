using System.Numerics;
using Veldrid;
using Whisperleaf.Graphics.Assets;
using Whisperleaf.Graphics.Loaders;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.RenderPasses
{
    public class GltfPass : IRenderPass, IDisposable
    {
        private readonly List<MeshGpu> _meshes = new();
        private readonly CameraUniformBuffer _cameraBuffer;
        private readonly Pipeline _pipeline;

        public GltfPass(GraphicsDevice gd, Camera camera, string modelPath)
        {
            var factory = gd.ResourceFactory;

            // Load model with Assimp loader
            var (cpuMeshes, cpuMaterials) = AssimpLoader.LoadCPU(modelPath, decodeImages: false);

            // Upload all meshes to GPU
            foreach (var mesh in cpuMeshes)
                _meshes.Add(new MeshGpu(gd, mesh));

            // Camera uniform
            _cameraBuffer = new CameraUniformBuffer(gd);

            // Vertex layout: must match loader output
            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
                new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4)
            );

            // Create pipeline (basic flat shader for now)
            _pipeline = PipelineFactory.CreatePipeline(
                gd,
                "Graphics/Shaders/Gltf.vert",
                "Graphics/Shaders/Gltf.frag",
                vertexLayout,
                gd.MainSwapchain.Framebuffer,
                enableDepth: true,
                enableBlend: false,
                extraLayouts: new[] { _cameraBuffer.Layout }
            );
        }

        public void Render(GraphicsDevice gd, CommandList cl, Camera? camera = null)
        {
            if (camera == null || _meshes.Count == 0)
                return;

            _cameraBuffer.Update(gd, camera);

            cl.Begin();
            cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
            cl.ClearColorTarget(0, RgbaFloat.CornflowerBlue);
            cl.ClearDepthStencil(1.0f);

            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);

            foreach (var mesh in _meshes)
            {
                cl.SetVertexBuffer(0, mesh.VertexBuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                cl.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
            }

            cl.End();
            gd.SubmitCommands(cl);
        }

        public void Dispose()
        {
            foreach (var m in _meshes) m.Dispose();
            _pipeline.Dispose();
            _cameraBuffer.Dispose();
        }
    }
}
