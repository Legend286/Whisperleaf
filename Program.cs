using Veldrid;
using Whisperleaf.Graphics;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Input;
using Whisperleaf.Platform;

namespace Whisperleaf;

class Program
{
    static void Main(string[] args)
    {
        var window = new Window(1280, 720, $"Whisperleaf Renderer");
        var input = new InputManager();
        var camera = new Camera(window.AspectRatio);
        var renderer = new Renderer(window);
        
        renderer.AddPass(new MeshPass(window.graphicsDevice, camera));
        renderer.SetCamera(camera);
        renderer.Run();
    }
}