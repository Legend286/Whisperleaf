using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Whisperleaf.Graphics.Scene.Data
{
    public class CameraUniformBuffer : IDisposable
    {
        private readonly DeviceBuffer _buffer;
        private readonly ResourceLayout _layout;
        private readonly ResourceSet _resourceSet;

        public CameraUniformBuffer(GraphicsDevice gd)
        {
            var factory = gd.ResourceFactory;

            _buffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<CameraUniform>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _buffer));
        }

        public void Update(GraphicsDevice gd, Camera camera)
        {
            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            var camPos = camera.Position;

            var data = new CameraUniform(view, proj, camPos);
            gd.UpdateBuffer(_buffer, 0, ref data);
        }

        public ResourceLayout Layout => _layout;
        public ResourceSet ResourceSet => _resourceSet;

        public void Dispose()
        {
            _buffer.Dispose();
            _layout.Dispose();
            _resourceSet.Dispose();
        }
    }
}