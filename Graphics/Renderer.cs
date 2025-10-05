using Veldrid;
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
    private Camera? _camera;
    private CameraController? _cameraController;
    private EditorManager? _editorManager;
    public Renderer(Window window)
    {
        _window = window;
        _cl = _window.graphicsDevice.ResourceFactory.CreateCommandList();
        PbrLayout.Initialize(_window.graphicsDevice);
        _editorManager = new EditorManager(_window.graphicsDevice, _window.SdlWindow);
    }

    public void AddPass(IRenderPass pass) => _passes.Add(pass);

    public void SetCamera(Camera camera)
    {
        _camera = camera;
        _cameraController = new CameraController(_camera, _window);
    }

    public void Run()
    {
        while (_window.Exists)
        {
            Time.Update();
            
            _cameraController.Update(Time.DeltaTime);
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
}