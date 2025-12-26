using System;
using System.IO;
using Veldrid;
using Whisperleaf.Graphics;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Platform;

namespace Whisperleaf;

class Program
{
    static void Main(string[] args)
    {
        var window = new Window(1920, 1080, $"Whisperleaf Renderer");
        var renderer = new Renderer(window);
        
        var testScene = new LightPerformanceTestScene(renderer);
        renderer.Run((dt) => testScene.Update(renderer));
    }
}
