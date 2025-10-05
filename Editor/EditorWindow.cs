using ImGuiNET;

namespace Whisperleaf.Editor;

/// <summary>
/// Base class for editor windows/panels
/// </summary>
public abstract class EditorWindow
{
    public string Title { get; protected set; } = "Window";
    public bool IsOpen = false;
    public ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    /// <summary>
    /// Called every frame to render the window
    /// </summary>
    public void Draw()
    {
        if (!IsOpen)
            return;

        if (ImGui.Begin(Title, ref IsOpen, WindowFlags))
        {
            OnDraw();
        }
        ImGui.End();
    }

    /// <summary>
    /// Override this to draw window contents
    /// </summary>
    protected abstract void OnDraw();
}
