using Veldrid;
using Whisperleaf.Graphics;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Platform;

namespace Whisperleaf;

class Program
{
    static void Main(string[] args)
    {
        var window = new Window(1280, 720, $"Whisperleaf Renderer");

        var renderer = new Renderer(window);

        renderer.AddPass(new ClearPass(RgbaFloat.CornflowerBlue));
        renderer.AddPass(new TrianglePass(window.graphicsDevice));
        renderer.Run();
    }
}