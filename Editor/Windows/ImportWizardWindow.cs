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

    private SceneAsset? _importedScene;
    private string? _importError;

    protected override void OnDraw()
    {
        if (string.IsNullOrEmpty(_sourcePath))
        {
            ImGui.TextDisabled("No file selected");
            return;
        }

        // Check for import completion
        if (_importedScene != null)
        {
            OnImportComplete?.Invoke(_importedScene);
            _importedScene = null;
            _importing = false;
            IsOpen = false;
        }
        
        if (_importError != null)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1, 0, 0, 1), $"Error: {_importError}");
            if (ImGui.Button("Close")) _importError = null;
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
            ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), "Importing... Please wait.");
            // Spinner or progress bar could go here
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
        _importError = null;

        string sourcePath = _sourcePath;
        float scale = _scaleFactor;

        Task.Run(() => 
        {
            try
            {
                // Determine output path
                var scenesDir = "Resources/Scenes";
                Directory.CreateDirectory(scenesDir);

                var fileName = Path.GetFileNameWithoutExtension(sourcePath);
                var outputPath = Path.Combine(scenesDir, $"{fileName}.wlscene");

                // Import
                var scene = SceneImporter.Import(sourcePath, outputPath, scale);
                _importedScene = scene;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import failed: {ex.Message}");
                _importError = ex.Message;
                _importing = false;
            }
        });
    }
}
