using Veldrid;
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
    private InputManager? _inputManager;
    public Renderer(Window window)
    {
        _window = window;
        _cl = _window.graphicsDevice.ResourceFactory.CreateCommandList();
        _inputManager = new InputManager();
        PbrLayout.Initialize(_window.graphicsDevice);
    }

    public void AddPass(IRenderPass pass) => _passes.Add(pass);

    public void SetCamera(Camera camera)
    {
        _camera = camera;
        _cameraController = new CameraController(_camera, _inputManager, _window);
    }

    public void Run()
    {
        while (_window.Exists)
        {
            Time.Update();
        
            _inputManager.Update(_window);
            _cameraController.Update(Time.DeltaTime);
            _window.PumpEvents();
            foreach (var pass in _passes)
            {
                pass.Render(_window.graphicsDevice, _cl, _camera);
            }

            _window.graphicsDevice.SwapBuffers(_window.graphicsDevice.MainSwapchain);
        }
    }
}