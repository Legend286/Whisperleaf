using System;
using Veldrid;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Editor;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Input;
using Whisperleaf.Platform;

namespace Whisperleaf.Graphics;

public class Renderer
{
    private readonly Window _window;
    private readonly CommandList _cl;
    private readonly List<IRenderPass> _passes = new();
    private readonly EditorManager _editorManager;
    private readonly GltfPass _scenePass;
    private Camera? _camera;
    private CameraController? _cameraController;
    public Renderer(Window window)
    {
        _window = window;
        _cl = _window.graphicsDevice.ResourceFactory.CreateCommandList();
        PbrLayout.Initialize(_window.graphicsDevice);
        _editorManager = new EditorManager(_window.graphicsDevice, _window.SdlWindow);

        _scenePass = new GltfPass(_window.graphicsDevice);
        _passes.Add(_scenePass);

        _editorManager.SceneRequested += OnSceneRequested;
        _window.WindowResized += _editorManager.WindowResized;
    }

    public void AddPass(IRenderPass pass) => _passes.Add(pass);

    public void SetCamera(Camera camera)
    {
        _camera = camera;
        _cameraController = new CameraController(_camera, _window);
    }

    public void LoadScene(SceneAsset scene)
    {
        _scenePass.LoadScene(scene);
    }

    public void LoadScene(string path)
    {
        try
        {
            _scenePass.LoadScene(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load scene '{path}': {ex.Message}");
        }
    }

    public void Run()
    {
        while (_window.Exists)
        {
            Time.Update();
            
            _cameraController?.Update(Time.DeltaTime);
            var snapshot = _window.PumpEvents();
            InputManager.Update(snapshot);
            _editorManager.Update(Time.DeltaTime, snapshot);
            foreach (var pass in _passes)
            {
                pass.Render(_window.graphicsDevice, _cl, _camera);
            }

            _editorManager.Render(_cl);

            _window.graphicsDevice.SwapBuffers(_window.graphicsDevice.MainSwapchain);
        }
    }

    private void OnSceneRequested(SceneAsset scene)
    {
        try
        {
            _scenePass.LoadScene(scene);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Renderer failed to load scene: {ex.Message}");
        }
    }
}
