using ImGuiNET;
using System.Numerics;
using Veldrid;
using Whisperleaf.Graphics;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Platform;

namespace Whisperleaf.Editor.Windows;

public class MaterialViewportWindow : EditorWindow
{
    private readonly MaterialPreviewRenderer _previewRenderer;
    private readonly CameraController _cameraController;
    private bool _isHovered;
    private bool _isFocused;
    private Func<Texture, IntPtr>? _getBinding;

    public MaterialViewportWindow(MaterialPreviewRenderer renderer, Window window)
    {
        Title = "Material Preview";
        IsOpen = true;
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        _previewRenderer = renderer;
        
        _cameraController = new CameraController(_previewRenderer.Camera, window);
        _cameraController.Mode = CameraControllerMode.Orbit;
    }

    public void Update(float deltaTime)
    {
        _cameraController.InputEnabled = _isHovered;
        _cameraController.Update(deltaTime);
        _previewRenderer.Update(deltaTime);
        _previewRenderer.Render();
    }
    
    public void Render()
    {
        _previewRenderer.Render();
    }
    
    public event Action<string>? OnMeshDropped;

    public void SetBindingCallback(Func<Texture, IntPtr> callback)
    {
        _getBinding = callback;
    }
    
    public void DrawContent()
    {
        OnDraw();
    }

    protected override void OnDraw()
    {
        var size = ImGui.GetContentRegionAvail();
        
        // Handle Resize
        if ((size.X != _previewRenderer.Width || size.Y != _previewRenderer.Height) && size.X > 0 && size.Y > 0)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _previewRenderer.Resize((uint)size.X, (uint)size.Y);
            }
        }

        var texture = _previewRenderer.GetTexture();
        if (texture != null && _getBinding != null)
        {
            IntPtr id = _getBinding(texture);
            
            ImGui.Image(id, size);
            
            // Drag Drop for Mesh
            if (ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var payload = ImGui.AcceptDragDropPayload("MODEL_ASSET");
                    if (payload.NativePtr != null)
                    {
                         string path = DragDropPayload.CurrentAssetPath;
                         if (!string.IsNullOrEmpty(path))
                         {
                             Console.WriteLine($"[MaterialViewport] Dropped mesh: {path}");
                             OnMeshDropped?.Invoke(path);
                         }
                    }
                }
                ImGui.EndDragDropTarget();
            }
        }
        
        _isHovered = ImGui.IsItemHovered();
        _isFocused = ImGui.IsWindowFocused();
    }
    
    // External setter for the texture binding ID
    // public IntPtr PreviewTextureId { get; set; } = IntPtr.Zero;
}
