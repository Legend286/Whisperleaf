using System.IO;
using Veldrid;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics;
using Whisperleaf.Graphics.RenderPasses;
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

       
        /*var defaultScenePath = Path.Combine("Resources", "Scenes", "Bistro_Godot.wlscene");
        if (File.Exists(defaultScenePath))
        {
            renderer.LoadScene(SceneAsset.Load(defaultScenePath));
        }*/
        
        renderer.AddPass(new GltfPass(window.graphicsDevice, camera ,"Resources/models/sponza-palace/source/scene.glb"));
        renderer.Run();
    }
}
