using Veldrid;

namespace Whisperleaf.Graphics.RenderPasses;

public interface IRenderPass
{
    void Render(GraphicsDevice gd, CommandList cl);
}