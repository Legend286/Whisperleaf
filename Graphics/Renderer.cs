using Veldrid;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Platform;

namespace Whisperleaf.Graphics;

public class Renderer
{
    private readonly Window _window;
    private readonly CommandList _cl;
    private readonly List<IRenderPass> _passes = new();
    private Camera? _camera;

    public Renderer(Window window)
    {
        _window = window;
        _cl = _window.graphicsDevice.ResourceFactory.CreateCommandList();
    }

    public void AddPass(IRenderPass pass) => _passes.Add(pass);
    public void SetCamera(Camera camera) => _camera = camera;

    public void Run()
    {
        while (_window.Exists)
        {
            _window.PumpEvents();

            foreach (var pass in _passes)
            {
                pass.Render(_window.graphicsDevice, _cl, _camera);
            }
            _window.graphicsDevice.WaitForIdle();
            _window.graphicsDevice.SwapBuffers(_window.graphicsDevice.MainSwapchain);
        }
    }
}