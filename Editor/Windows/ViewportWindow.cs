using System.Numerics;
using ImGuiNET;
using Veldrid;
using Whisperleaf.Graphics.RenderPasses;

namespace Whisperleaf.Editor.Windows;

public class ViewportWindow : EditorWindow
{
    private readonly GltfPass _scenePass;
    private readonly ImGuiController _imGuiController;
    private readonly GraphicsDevice _gd;
    private Vector2 _lastSize;

    public bool IsFocused { get; private set; }
    public bool IsHovered { get; private set; }
    public Vector2 ViewportSize => _lastSize;
    public Vector2 ViewportPos { get; private set; }
    
    public event Action<Vector2>? OnResize;
    public Action? OnDrawGizmo;

    public ViewportWindow(GltfPass scenePass, ImGuiController imGuiController, GraphicsDevice gd)
    {
        Title = "Viewport";
        IsOpen = true;
        _scenePass = scenePass;
        _imGuiController = imGuiController;
        _gd = gd;
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    protected override void OnDraw()
    {
        var size = ImGui.GetContentRegionAvail();
        ViewportPos = ImGui.GetCursorScreenPos();

        // Check for resize
        if (size.X > 0 && size.Y > 0)
        {
            if (size != _lastSize)
            {
                _lastSize = size;
                OnResize?.Invoke(_lastSize);
            }

            uint targetW = (uint)size.X;
            uint targetH = (uint)size.Y;
            uint currentW = _scenePass.OutputTexture?.Width ?? 0;
            uint currentH = _scenePass.OutputTexture?.Height ?? 0;

            bool sizeMismatch = targetW != currentW || targetH != currentH;
            bool isMouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);

            if (sizeMismatch && (!isMouseDown || _scenePass.OutputTexture == null))
            {
                if (_scenePass.OutputTexture != null)
                {
                    _imGuiController.RemoveImGuiBinding(_scenePass.OutputTexture);
                }
                _scenePass.Resize(targetW, targetH);
            }
        }

        if (_scenePass.OutputTexture != null)
        {
            var ptr = _imGuiController.GetOrCreateImGuiBinding(_gd.ResourceFactory, _scenePass.OutputTexture);
            ImGui.Image(ptr, size);
            OnDrawGizmo?.Invoke();
        }
        
        IsFocused = ImGui.IsWindowFocused();
        IsHovered = ImGui.IsWindowHovered();
    }
}
