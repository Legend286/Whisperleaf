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
        var window = new Window(1280, 720, $"Whisperleaf Renderer");
        var camera = new Camera(window.AspectRatio);
        var renderer = new Renderer(window);
        renderer.SetCamera(camera);

        // var defaultScenePath = Path.Combine("Resources", "Scenes", "Bistro_Godot.wlscene");
        // if (File.Exists(defaultScenePath))
        // {
        //     renderer.LoadScene(defaultScenePath);
        // }
        // else
        // {
        //     Console.WriteLine($"No default scene found at '{defaultScenePath}'. Use the editor to import a scene.");
        // }
        
        var physicsScene = new Whisperleaf.Physics.PhysicsTestScene(renderer);
        renderer.Run((dt) => physicsScene.Update(renderer));
    }
}
