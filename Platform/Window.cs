using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Whisperleaf.Platform;

public class Window
{
    private Sdl2Window sdlWindow;

    public GraphicsDevice graphicsDevice;

    public Window(int width, int height, string title)
    {
        GraphicsDeviceOptions gdopt = new GraphicsDeviceOptions{
            Debug = false,
            HasMainSwapchain = true,
            PreferDepthRangeZeroToOne = true, 
            PreferStandardClipSpaceYDirection = true, 
            ResourceBindingModel = ResourceBindingModel.Improved,
            SwapchainDepthFormat = PixelFormat.D32_Float_S8_UInt,
            SwapchainSrgbFormat = true,
            SyncToVerticalBlank = true,
        };

        GraphicsBackend preferredBackend = GraphicsBackend.OpenGL;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            preferredBackend = GraphicsBackend.Vulkan;
        }
        else if (OperatingSystem.IsMacOS())
        {
            preferredBackend = GraphicsBackend.Metal;
        }
        
        var wci = new WindowCreateInfo
        {
            X = 100,
            Y = 100,
            WindowWidth = width,
            WindowHeight = height,
            WindowTitle = $"{title} ({preferredBackend})"
        };
        
        VeldridStartup.CreateWindowAndGraphicsDevice(wci, gdopt, preferredBackend, out sdlWindow, out graphicsDevice);
    }

    public bool Exists => sdlWindow.Exists;
    public void PumpEvents() => sdlWindow.PumpEvents();
}