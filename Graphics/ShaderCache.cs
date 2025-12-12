using Veldrid;
using Veldrid.SPIRV;

namespace Whisperleaf.Graphics
{
    public static class ShaderCache
    {
        private static readonly Dictionary<(string, string), (byte[], byte[])> _shaderBytes = new();

        public static (byte[] vsBytes, byte[] fsBytes) GetShaderBytes(string vertexPath, string fragmentPath)
        {
            var key = (vertexPath, fragmentPath);
            if (_shaderBytes.TryGetValue(key, out var bytes))
                return bytes;

            byte[] vertexCode = File.ReadAllBytes(vertexPath);
            byte[] fragmentCode = File.ReadAllBytes(fragmentPath); // ReadBytes is better than ReadAllText for potential binary spirv, but here we used ReadAllText before. Assuming source is GLSL/HLSL text or binary?
            // Original used ReadAllText and Encoding.UTF8.GetBytes.
            // Let's stick to that if it's text.
            // Wait, previous code: File.ReadAllText -> GetBytes.
            // Let's stick to that to be safe.
            
            vertexCode = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(vertexPath));
            fragmentCode = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(fragmentPath));

            var result = (vertexCode, fragmentCode);
            _shaderBytes[key] = result;
            return result;
        }

        public static void Clear()
        {
            _shaderBytes.Clear();
        }
    }
}