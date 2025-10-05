using ImGuiNET;
using Whisperleaf.AssetPipeline.Scene;

namespace Whisperleaf.Editor.Windows;

/// <summary>
/// Asset browser for .wlscene files
/// </summary>
public class AssetBrowserWindow : EditorWindow
{
    private string _currentPath = "Resources/Scenes";
    private List<string> _sceneFiles = new();
    private string? _selectedFile;

    public event Action<SceneAsset>? OnSceneSelected;

    public AssetBrowserWindow()
    {
        Title = "Asset Browser";
        RefreshAssets();
    }

    protected override void OnDraw()
    {
        // Toolbar
        if (ImGui.Button("Refresh"))
        {
            RefreshAssets();
        }

        ImGui.SameLine();
        ImGui.Text($"Path: {_currentPath}");

        ImGui.Separator();

        // File list
        foreach (var file in _sceneFiles)
        {
            var fileName = Path.GetFileName(file);
            bool isSelected = _selectedFile == file;

            if (ImGui.Selectable($"📦 {fileName}", isSelected, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedFile = file;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    LoadScene(file);
                }
            }

            // Context menu
            if (ImGui.BeginPopupContextItem($"ctx_{fileName}"))
            {
                if (ImGui.MenuItem("Load"))
                {
                    LoadScene(file);
                }
                if (ImGui.MenuItem("Show in Finder"))
                {
                    ShowInFinder(file);
                }
                ImGui.EndPopup();
            }
        }

        // Drag-drop target
        ImGui.Separator();
        ImGui.TextDisabled("Drop glTF/GLB files here to import");

        if (ImGui.BeginDragDropTarget())
        {
            // TODO: Handle file drop
            ImGui.EndDragDropTarget();
        }
    }

    private void RefreshAssets()
    {
        _sceneFiles.Clear();

        if (!Directory.Exists(_currentPath))
        {
            Directory.CreateDirectory(_currentPath);
        }

        _sceneFiles.AddRange(Directory.GetFiles(_currentPath, "*.wlscene", SearchOption.AllDirectories));
    }

    private void LoadScene(string path)
    {
        try
        {
            var scene = SceneAsset.Load(path);
            OnSceneSelected?.Invoke(scene);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load scene: {ex.Message}");
        }
    }

    private void ShowInFinder(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start("open", $"-R \"{path}\"");
        }
        else if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start("explorer", $"/select,\"{path}\"");
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", Path.GetDirectoryName(path) ?? "");
        }
    }
}
