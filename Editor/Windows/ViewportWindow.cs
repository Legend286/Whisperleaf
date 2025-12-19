using ImGuiNET;
using System.Numerics;
using Whisperleaf.Graphics;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Platform;
using Veldrid;

namespace Whisperleaf.Editor.Windows;

public class ViewportWindow : EditorWindow
{
    private readonly Renderer _renderer;
    private bool _isHovered;
    private bool _isFocused;
    
    public Camera Camera { get; private set; }
    public CameraController CameraController { get; private set; }

    public bool IsHovered => _isHovered;
    public bool IsFocused => _isFocused;
    public Vector2 Position { get; private set; }
    public Vector2 Size { get; private set; }

    public ViewportWindow(Renderer renderer, Window window)
    {
        Title = "Game View";
        IsOpen = true;
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        _renderer = renderer;
        
        Camera = new Camera(window.AspectRatio);
        CameraController = new CameraController(Camera, window);
    }

    public void Update(float deltaTime)
    {
        if (CameraController != null)
        {
            CameraController.InputEnabled = _isHovered;
            CameraController.Update(deltaTime);
        }
    }

    protected override void OnDraw()
    {
        var size = ImGui.GetContentRegionAvail();
        Size = size;
        var pos = ImGui.GetCursorScreenPos();
        Position = pos;
        
        // Handle Resize
        // We only resize the framebuffer when the mouse is released to avoid lag
        if ((size.X != _renderer.ViewportWidth || size.Y != _renderer.ViewportHeight) && size.X > 0 && size.Y > 0)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _renderer.ResizeViewport((uint)size.X, (uint)size.Y);
            }
        }
        
        // Always update aspect ratio
        if (size.X > 0 && size.Y > 0)
        {
            Camera.AspectRatio = size.X / size.Y;
        }

        var textureId = _renderer.GetGameViewTextureId();
        if (textureId != IntPtr.Zero)
        {
            ImGui.Image(textureId, size);
            _renderer.DrawGizmo();
        }

        _isHovered = ImGui.IsItemHovered();
        _isFocused = ImGui.IsWindowFocused();
    }
}
