using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.Graphics.Assets;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.Loaders;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.RenderPasses
{
    public class GltfPass : IRenderPass
    {
        private readonly Pipeline _pipeline;
        private readonly CameraUniformBuffer _cameraBuffer;

        private readonly List<MeshGpu> _meshes = new();
        private readonly List<MaterialData> _materials = new();

        public GltfPass(GraphicsDevice gd, Camera camera, string modelPath)
        {
            var factory = gd.ResourceFactory;

            // Load from AssimpLoader (CPU)
            var (cpuMeshes, cpuMats, scene) = AssimpLoader.LoadCPU(modelPath);

            // Upload meshes to GPU
            foreach (var mesh in cpuMeshes)
                _meshes.Add(new MeshGpu(gd, mesh));
            
            foreach (var mat in cpuMats)
            {
                MaterialUploader.Upload(gd, PbrLayout.MaterialLayout, mat, scene);
                _materials.Add(mat);
            }


            _cameraBuffer = new CameraUniformBuffer(gd);

            // Pipeline
            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
                new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4),
                new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate,
                    VertexElementFormat.Float2)
            );

            _pipeline = PipelineFactory.CreatePipeline(
                gd,
                "Graphics/Shaders/PBR.vert",
                "Graphics/Shaders/PBR.frag",
                vertexLayout,
                gd.MainSwapchain.Framebuffer,
                enableDepth: true,
                enableBlend: false,
                extraLayouts: new[] { _cameraBuffer.Layout, PbrLayout.MaterialLayout }
            );
        }

        public void Render(GraphicsDevice gd, CommandList cl, Camera? camera = null)
        {
            if (camera == null) return;

            _cameraBuffer.Update(gd, camera);

            cl.Begin();
            cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
            cl.ClearColorTarget(0, RgbaFloat.Black);
            cl.ClearDepthStencil(1f);

            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);

            for (int i = 0; i < _meshes.Count; i++)
            {
                var mesh = _meshes[i];
                var mat = _materials[Math.Min(i, _materials.Count - 1)];

                cl.SetVertexBuffer(0, mesh.VertexBuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);

                // Bind material
                if (mat.ResourceSet != null)
                    cl.SetGraphicsResourceSet(1, mat.ResourceSet);
                
                cl.DrawIndexed((uint)mesh.IndexCount, 1, 0, 0, 0);
            }


            cl.End();
            gd.SubmitCommands(cl);
        }
    }
}