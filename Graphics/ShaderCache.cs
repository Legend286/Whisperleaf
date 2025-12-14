using Veldrid;
using Veldrid.SPIRV;

namespace Whisperleaf.Graphics
{
    public static class ShaderCache
    {
        private static readonly Dictionary<(string, string), Shader[]> _shaderPairs = new();

        public static Shader[] GetShaderPair(GraphicsDevice gd, string vertexPath, string fragmentPath)
        {
            var key = (vertexPath, fragmentPath);
            if (_shaderPairs.TryGetValue(key, out var shaders))
                return shaders;

            string vertexCode = File.ReadAllText(vertexPath);
            string fragmentCode = File.ReadAllText(fragmentPath);

            shaders = gd.ResourceFactory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main"),
                new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main")
            );

            _shaderPairs[key] = shaders;
            return shaders;
        }

        public static void DisposeAll()
        {
            foreach (var pair in _shaderPairs.Values)
            {
                foreach (var shader in pair)
                {
                    shader.Dispose();
                }
            }
            _shaderPairs.Clear();
        }
    }
}