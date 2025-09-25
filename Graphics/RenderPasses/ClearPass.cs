using Veldrid;

namespace Whisperleaf.Graphics.RenderPasses;

public class ClearPass : IRenderPass
{
    private readonly RgbaFloat _clearColor;

    public ClearPass(RgbaFloat clearColor)
    {
        _clearColor = clearColor;
    }
    
    public void Render(GraphicsDevice gd, CommandList cl)
    {
        cl.Begin();
        cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
        cl.ClearColorTarget(0, _clearColor);
        cl.End();
        gd.SubmitCommands(cl);
    }
}