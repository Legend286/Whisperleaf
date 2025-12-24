using System.Numerics;
using ImGuiNET;
using Whisperleaf.AssetPipeline;
using Whisperleaf.Platform;

using Whisperleaf.AssetPipeline.Cache;

namespace Whisperleaf.Editor.Windows;

public class MaterialEditorWindow : EditorWindow
{
    private readonly ThumbnailGenerator _thumbs;
    private MaterialAsset _currentMaterial;
    private string? _currentPath;
    private bool _dirty;
    
    public event Action<string, MaterialAsset>? MaterialChanged;
    public event Action<string>? RevealRequested;

    public MaterialEditorWindow(ThumbnailGenerator thumbs)
    {
        Title = "Material Editor";
        _thumbs = thumbs;
        IsOpen = false; // Closed by default
        _currentMaterial = new MaterialAsset();
    }

    public void OpenMaterial(string path)
    {
        try
        {
            _currentMaterial = MaterialAsset.Load(path);
            _currentPath = path;
            _dirty = false;
            IsOpen = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialEditor] Failed to load material: {ex.Message}");
        }
    }

    private void NewMaterial()
    {
        _currentMaterial = new MaterialAsset();
        _currentPath = null;
        _dirty = false;
    }

    private void SaveMaterial()
    {
        if (string.IsNullOrEmpty(_currentPath))
        {
            // Ideally open SaveDialog, but for now we might need to rely on a default or manual entry
            // Since we don't have a native file dialog easily available without a wrapper, 
            // we will save to a default location or require SaveAs logic if path is null.
            Console.WriteLine("[MaterialEditor] Use 'Save As' to save a new material.");
            return;
        }

        _currentMaterial.Save(_currentPath);
        _dirty = false;
        Console.WriteLine($"[MaterialEditor] Saved to {_currentPath}");
    }

    private void SaveMaterialAs(string filename)
    {
        // Simple hack: Save to "Resources/Materials/" + filename
        // In a real app we'd pop a dialog. 
        // For this prototype, we'll assume the user might have to manually manage paths or we implement a simple input popup.
        
        string dir = Path.Combine("Resources", "Materials");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        string path = Path.Combine(dir, filename.EndsWith(".wlmat") ? filename : filename + ".wlmat");
        _currentMaterial.Save(path);
        _currentPath = path;
        _dirty = false;
        Console.WriteLine($"[MaterialEditor] Saved to {path}");
    }

    protected override void OnDraw()
    {
        DrawMenuBar();

        if (_currentMaterial == null) return;
        
        bool changed = false;
        
        // Dirty indicator
        if (_dirty) ImGui.TextColored(new Vector4(1, 1, 0, 1), "* Unsaved Changes");
        
        if (ImGui.Button("Save"))
        {
            if (_currentPath != null)
                SaveMaterial();
            else
                Console.WriteLine("[MaterialEditor] No file path set. Use 'File > Save As'.");
        }
        ImGui.SameLine();

        string name = _currentMaterial.Name;
        if (ImGui.InputText("Name", ref name, 64))
        {
            _currentMaterial.Name = name;
            changed = true;
        }

        ImGui.Separator();
        ImGui.Text("PBR Factors");

        Vector4 baseColor = _currentMaterial.BaseColorFactor;
        if (ImGui.ColorEdit4("Base Color", ref baseColor))
        {
            _currentMaterial.BaseColorFactor = baseColor;
            changed = true;
        }

        Vector3 emissive = _currentMaterial.EmissiveFactor;
        if (ImGui.ColorEdit3("Emissive", ref emissive))
        {
            _currentMaterial.EmissiveFactor = emissive;
            changed = true;
        }

        float metallic = _currentMaterial.MetallicFactor;
        if (ImGui.SliderFloat("Metallic", ref metallic, 0, 1))
        {
            _currentMaterial.MetallicFactor = metallic;
            changed = true;
        }

        float roughness = _currentMaterial.RoughnessFactor;
        if (ImGui.SliderFloat("Roughness", ref roughness, 0, 1))
        {
            _currentMaterial.RoughnessFactor = roughness;
            changed = true;
        }

        ImGui.Separator();
        ImGui.Text("Alpha Settings");

        int alphaMode = (int)_currentMaterial.AlphaMode;
        string[] modes = { "Opaque", "Mask", "Blend" };
        if (ImGui.Combo("Alpha Mode", ref alphaMode, modes, modes.Length))
        {
            _currentMaterial.AlphaMode = (AlphaMode)alphaMode;
            changed = true;
        }

        if (_currentMaterial.AlphaMode == AlphaMode.Mask)
        {
            float cutoff = _currentMaterial.AlphaCutoff;
            if (ImGui.SliderFloat("Alpha Cutoff", ref cutoff, 0, 1))
            {
                _currentMaterial.AlphaCutoff = cutoff;
                changed = true;
            }
        }

        ImGui.Separator();
        ImGui.Text("Textures (Drag & Drop from Asset Browser)");

        string? newBase = DrawTextureSlot("Base Color", _currentMaterial.BaseColorTexture);
        if (newBase != _currentMaterial.BaseColorTexture) { _currentMaterial.BaseColorTexture = newBase; changed = true; }
        
        string? newNorm = DrawTextureSlot("Normal", _currentMaterial.NormalTexture);
        if (newNorm != _currentMaterial.NormalTexture) { _currentMaterial.NormalTexture = newNorm; changed = true; }
        
        string? newRMA = DrawTextureSlot("RMA (Rough/Metal/AO)", _currentMaterial.RMATexture);
        if (newRMA != _currentMaterial.RMATexture) { _currentMaterial.RMATexture = newRMA; changed = true; }
        
        string? newEmis = DrawTextureSlot("Emissive", _currentMaterial.EmissiveTexture);
        if (newEmis != _currentMaterial.EmissiveTexture) { _currentMaterial.EmissiveTexture = newEmis; changed = true; }
        
        if (changed)
        {
            _dirty = true;
            if (_currentPath != null)
                MaterialChanged?.Invoke(_currentPath, _currentMaterial);
        }
    }

    private string? DrawTextureSlot(string label, string? path)
    {
        ImGui.PushID(label);
        ImGui.Text(label);
        
        IntPtr texHandle = IntPtr.Zero;
        if (!string.IsNullOrEmpty(path))
        {
            texHandle = _thumbs.GetThumbnail(path, AssetType.Texture);
        }
        
        Vector2 size = new Vector2(64, 64);
        bool clicked = false;
        
        ImGui.BeginGroup(); // Wrap in group for robust drag target
        if (texHandle != IntPtr.Zero)
        {
            // Image Button
            if (ImGui.ImageButton(label + "_btn", texHandle, size)) clicked = true;
        }
        else
        {
            // Placeholder Button
            string btnText = string.IsNullOrEmpty(path) ? "None" : "Loading...";
            if (ImGui.Button(btnText, size)) clicked = true;
        }
        ImGui.EndGroup();

        // Drag Drop Target
        if (ImGui.BeginDragDropTarget())
        {
            unsafe {
                var payload = ImGui.AcceptDragDropPayload("TEXTURE_ASSET");
                if (payload.NativePtr != null && payload.DataSize > 0)
                {
                    string droppedPath = System.Text.Encoding.UTF8.GetString((byte*)payload.Data, payload.DataSize);
                    string ext = Path.GetExtension(droppedPath).ToLower();
                    if (ext == ".wltex" || ext == ".png" || ext == ".jpg" || ext == ".tga")
                    {
                        path = droppedPath;
                        _dirty = true;
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }
        
        // Double click to reveal
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (!string.IsNullOrEmpty(path)) RevealRequested?.Invoke(path);
        }
        
        // Tooltip
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(path))
        {
            ImGui.SetTooltip(Path.GetFileName(path));
        }

        ImGui.SameLine();
        if (ImGui.Button("X"))
        {
            path = null;
            _dirty = true;
        }

        if (!string.IsNullOrEmpty(path))
        {
            ImGui.TextDisabled(Path.GetFileName(path));
        }

        ImGui.PopID();
        ImGui.Spacing();
        return path;
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New"))
                {
                    NewMaterial();
                }

                if (ImGui.MenuItem("Save", _currentPath != null))
                {
                    SaveMaterial();
                }
                
                // Hacky "Save As" input
                ImGui.Separator();
                ImGui.Text("Save As (Resources/Materials/):");
                // We can't do a text input inside a menu item easily without closing it
                // So we'll trigger a popup or just rely on a separate window section for "Save As"
                // For now, let's keep it simple: If you click Save As, we print to console instruction
                // or we use a static modal.
                
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }
    }
}
