using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.Graphics.Scene.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CameraUniform
    {
        public Matrix4x4 View;
        public Matrix4x4 Proj;
        public Matrix4x4 ViewProjection;
        public Vector3 CameraPos;
        private float _padding; // 16-byte alignment for std140

        public CameraUniform(Matrix4x4 view, Matrix4x4 proj, Vector3 camPos)
        {
            View = view;
            Proj = proj;
            ViewProjection = view * proj;
            CameraPos = camPos;
            _padding = 0;
        }
    }
}