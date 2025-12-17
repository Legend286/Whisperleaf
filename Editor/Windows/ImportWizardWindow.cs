using ImGuiNET;
using Whisperleaf.AssetPipeline.Scene;

namespace Whisperleaf.Editor.Windows;

/// <summary>
/// Model import wizard with preview and settings
/// </summary>
public class ImportWizardWindow : EditorWindow
{
    private string _sourcePath = "";
    private float _scaleFactor = 1.0f;
    private SceneMetadata? _preview;
    private bool _importing = false;

    public event Action<SceneAsset>? OnImportComplete;

    public ImportWizardWindow()
    {
        Title = "Import Model";
        WindowFlags = ImGuiWindowFlags.NoDocking;
    }

    public void Open(string gltfPath)
    {
        _sourcePath = gltfPath;
        _scaleFactor = 1.0f;
        _importing = false;
        IsOpen = true;

        // Load preview
        try
        {
            _preview = SceneImporter.GetImportPreview(gltfPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Preview failed: {ex.Message}");
            _preview = null;
        }
    }

    protected override void OnDraw()
    {
        if (string.IsNullOrEmpty(_sourcePath))
        {
            ImGui.TextDisabled("No file selected");
            return;
        }

        ImGui.Text($"Source: {Path.GetFileName(_sourcePath)}");
        ImGui.Separator();

        // Preview info
        if (_preview != null)
        {
            ImGui.Text("Model Preview:");
            ImGui.Indent();

            var size = _preview.BoundsSize;
            ImGui.Text($"Dimensions: {size.X:F2} x {size.Y:F2} x {size.Z:F2} units");
            ImGui.Text($"Meshes: {_preview.TotalMeshCount}");
            ImGui.Text($"Vertices: {_preview.TotalVertexCount:N0}");
            ImGui.Text($"Triangles: {_preview.TotalTriangleCount:N0}");

            ImGui.Unindent();
            ImGui.Separator();
        }

        // Import settings
        ImGui.Text("Import Settings:");
        ImGui.Indent();

        if (ImGui.DragFloat("Scale Factor", ref _scaleFactor, 0.01f, 0.001f, 1000.0f, "%.3f"))
        {
            _scaleFactor = Math.Max(0.001f, _scaleFactor);
        }

        if (_preview != null && _scaleFactor != 1.0f)
        {
            var scaledSize = _preview.BoundsSize * _scaleFactor;
            ImGui.TextDisabled($"→ Scaled: {scaledSize.X:F2} x {scaledSize.Y:F2} x {scaledSize.Z:F2} units");
        }

        ImGui.Unindent();
        ImGui.Separator();

        // Import button
        if (_importing)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), "Importing...");
        }
        else
        {
            if (ImGui.Button("Import", new System.Numerics.Vector2(120, 0)))
            {
                Import();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new System.Numerics.Vector2(120, 0)))
            {
                IsOpen = false;
            }
        }
    }
    
    private void Import()
    {
        if (_importing || string.IsNullOrEmpty(_sourcePath))
            return;

        _importing = true;

        try
        {
            // Determine output path
            var scenesDir = "Resources/Scenes";
            Directory.CreateDirectory(scenesDir);

            var fileName = Path.GetFileNameWithoutExtension(_sourcePath);
            var outputPath = Path.Combine(scenesDir, $"{fileName}.wlscene");

            // Import
            var scene = SceneImporter.Import(_sourcePath, outputPath, _scaleFactor);

            OnImportComplete?.Invoke(scene);

            IsOpen = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Import failed: {ex.Message}");
        }
        finally
        {
            _importing = false;
        }
    }
}
