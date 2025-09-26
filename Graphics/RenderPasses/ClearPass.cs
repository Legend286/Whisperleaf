using Veldrid;
using Whisperleaf.Graphics.Scene;

namespace Whisperleaf.Graphics.RenderPasses;

public class ClearPass : IRenderPass
{
    private readonly RgbaFloat _clearColor;

    public ClearPass(RgbaFloat clearColor)
    {
        _clearColor = clearColor;
    }
    
    public void Render(GraphicsDevice gd, CommandList cl, Camera camera = null)
    {
        cl.Begin();
        cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
        cl.ClearColorTarget(0, _clearColor);
        cl.End();
        gd.SubmitCommands(cl);
    }
}