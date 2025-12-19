using Veldrid;
using Veldrid.SPIRV;

namespace Whisperleaf.Graphics
{
    public static class ShaderCache
    {
        private static readonly Dictionary<(string, string), Shader[]> _shaderPairs = new();
        private static readonly Dictionary<string, Shader> _shaders = new();

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

        public static Shader GetShader(GraphicsDevice gd, ShaderStages stage, string path)
        {
            if (_shaders.TryGetValue(path, out var shader))
                return shader;

            string code = File.ReadAllText(path);
            shader = gd.ResourceFactory.CreateFromSpirv(
                new ShaderDescription(stage, System.Text.Encoding.UTF8.GetBytes(code), "main")
            );

            _shaders[path] = shader;
            return shader;
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
            
            foreach (var shader in _shaders.Values)
            {
                shader.Dispose();
            }
            _shaders.Clear();
        }
    }
}