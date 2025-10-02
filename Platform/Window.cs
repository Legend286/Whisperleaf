using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Whisperleaf.Platform;

public class Window
{
    private Sdl2Window sdlWindow;

    public GraphicsDevice graphicsDevice;

    public int Width => sdlWindow.Width;
    public int Height => sdlWindow.Height;

    public float AspectRatio => (float)Width / Height;

    private GraphicsBackend Backend;

    public Window(int width, int height, string title)
    {
        GraphicsDeviceOptions gdopt = new GraphicsDeviceOptions
        {
            Debug = false,
            HasMainSwapchain = true,
            PreferDepthRangeZeroToOne = true,
            PreferStandardClipSpaceYDirection = true,
            ResourceBindingModel = ResourceBindingModel.Improved,
            SwapchainDepthFormat = PixelFormat.D32_Float_S8_UInt,
            SwapchainSrgbFormat = true,
            SyncToVerticalBlank = true,
        };

        Backend = GraphicsBackend.OpenGL;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            Backend = GraphicsBackend.Vulkan;
        }
        else if (OperatingSystem.IsMacOS())
        {
            Backend = GraphicsBackend.Metal;
        }

        var wci = new WindowCreateInfo
        {
            X = 100,
            Y = 100,
            WindowWidth = width,
            WindowHeight = height,
            WindowTitle = $"{title} ({Backend})"
        };

        VeldridStartup.CreateWindowAndGraphicsDevice(wci, gdopt, Backend, out sdlWindow, out graphicsDevice);
    }

    public bool Exists => sdlWindow.Exists;
    public InputSnapshot PumpEvents() => sdlWindow.PumpEvents();
    
    public (int X, int Y) GetMousePosition => ((int)PumpEvents().MousePosition.X,  (int)PumpEvents().MousePosition.Y);
    public void SetMousePosition(int x, int y) => sdlWindow.SetMousePosition(x, y);
    public void ShowCursor(bool visible) => sdlWindow.CursorVisible = visible;
    
    public void SetWindowTitle(string title) => sdlWindow.Title = $"{title} ({Backend}) (FPS: {Time.FPS} Frametime: {1/Time.DeltaTime:00} ms.";
}