using System.Numerics;
using Veldrid;
using Whisperleaf.Graphics.Scene;

namespace Whisperleaf.Graphics.RenderPasses;

public interface IRenderPass
{
    void Render(GraphicsDevice gd, CommandList cl, Camera? camera, Vector2 screenSize, int debugMode);
}