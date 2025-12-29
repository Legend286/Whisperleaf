using System.Numerics;
using ImGuiNET;
using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.Graphics;
using Whisperleaf.Platform;

using Whisperleaf.AssetPipeline.Cache;

namespace Whisperleaf.Editor.Windows;

public class MaterialEditorWindow : EditorWindow, IDisposable
{
    private readonly ThumbnailGenerator _thumbs;
    private readonly MaterialPreviewRenderer _previewRenderer;
    private readonly GraphicsDevice _gd; // To get ImGuiBinding via EditorManager (passed in constructor?)
    // Actually we need EditorManager to get texture binding.
    // Or we can just access ImGuiController if we had it.
    // EditorManager is not static.
    // We can pass a callback "GetTextureBinding" to the window.
    
    // Simplest: Pass EditorManager to MaterialEditorWindow? Or just the binding callback.
    // But we already have _thumbnailGenerator which holds EditorManager?
    // Let's check ThumbnailGenerator. It has 'private EditorManager _editor'.
    // We can't access it.
    
    // Let's modify MaterialEditorWindow to take a Func for binding.
    private Func<Texture, IntPtr> _getBinding;

    private MaterialAsset _currentMaterial;
    private string? _currentPath;
    private int _sceneMaterialIndex = -1;
    private bool _dirty;
    
    private bool _showSaveAs;
    private string _saveAsName = "NewMaterial";
    
    public event Action<string?, MaterialAsset, int>? MaterialChanged;
    public event Action<string, MaterialAsset, int>? MaterialSaved;
    public event Action<string>? RevealRequested;
    
    public MaterialPreviewRenderer PreviewRenderer => _previewRenderer;
    public MaterialViewportWindow Viewport { get; private set; }

    public MaterialEditorWindow(GraphicsDevice gd, Renderer renderer, ThumbnailGenerator thumbs)
    {
        Title = "Material Editor";
        _thumbs = thumbs;
        _gd = gd;
        IsOpen = false; // Closed by default
        _currentMaterial = new MaterialAsset();
        _previewRenderer = new MaterialPreviewRenderer(gd);
        Viewport = new MaterialViewportWindow(_previewRenderer, Window.Instance);
        
        Viewport.OnMeshDropped += meshPath =>
        {
            try
            {
                var data = WlMeshFormat.Read(meshPath, out string hash);
                Console.WriteLine($"[MaterialEditor] Loading mesh: {meshPath} (Hash: {hash})");
                _previewRenderer.SetPreviewMesh(meshPath);

                // Find material in registry
                if (AssetCache.TryGetMeshMetadata(hash, out var meta) && !string.IsNullOrEmpty(meta.MaterialPath))
                {
                    Console.WriteLine($"[MaterialEditor] Found associated material in registry: {meta.MaterialPath}");
                    OpenMaterial(meta.MaterialPath);
                }
                else
                {
                    Console.WriteLine("[MaterialEditor] No associated material found in registry for this mesh.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MaterialEditor] Error loading mesh on drop: {ex.Message}");
            }
        };
    }
    
    public void SetBindingCallback(Func<Texture, IntPtr> callback)
    {
        _getBinding = callback;
        Viewport.SetBindingCallback(callback);
    }

    public void OpenMaterial(string path)
    {
        try
        {
            Console.WriteLine($"[MaterialEditor] Opening material: {path}");
            _currentMaterial = MaterialAsset.Load(path);
            _currentPath = path;
            _sceneMaterialIndex = -1; // Unknown
            _dirty = false;
            IsOpen = true;
            _previewRenderer.UpdateMaterial(_currentMaterial);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialEditor] Failed to load material: {ex.Message}");
        }
    }

    public void OpenMaterial(MaterialAsset asset, int sceneIndex, string? path = null)
    {
        _currentMaterial = asset;
        _currentPath = path;
        _sceneMaterialIndex = sceneIndex;
        _dirty = false;
        IsOpen = true;
        _previewRenderer.UpdateMaterial(_currentMaterial);
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
        MaterialSaved?.Invoke(_currentPath, _currentMaterial, _sceneMaterialIndex);
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
        MaterialSaved?.Invoke(path, _currentMaterial, _sceneMaterialIndex);
        Console.WriteLine($"[MaterialEditor] Saved to {path}");
    }

    protected override void OnDraw()
    {
        DrawMenuBar();
        
        if (ImGui.BeginTable("MatEditorLayout", 2, ImGuiTableFlags.Resizable))
        {
            ImGui.TableNextColumn();
            DrawSettings();
            
            ImGui.TableNextColumn();
            Viewport.DrawContent();
            
            ImGui.EndTable();
        }
        
        if (_showSaveAs)
        {
            ImGui.OpenPopup("Save Material As");
            _showSaveAs = false; // Reset trigger, popup handles state
        }

        if (ImGui.BeginPopupModal("Save Material As", ref _showSaveAs, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("Name", ref _saveAsName, 64);
            
            if (ImGui.Button("Save", new Vector2(120, 0)))
            {
                string dir = string.IsNullOrEmpty(_currentPath) ? Path.Combine("Resources", "Materials") : Path.GetDirectoryName(_currentPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                string filename = _saveAsName.EndsWith(".wlmat") ? _saveAsName : _saveAsName + ".wlmat";
                string path = Path.Combine(dir, filename);
                
                _currentMaterial.Name = _saveAsName; // Sync name
                _currentMaterial.Save(path);
                _currentPath = path;
                _dirty = false;
                
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawSettings()
    {
        if (_currentMaterial == null) return;
        
        bool changed = false;
        
        // Dirty indicator
        if (_dirty) ImGui.TextColored(new Vector4(1, 1, 0, 1), "* Unsaved Changes");
        
        string name = _currentMaterial.Name;
        if (ImGui.Button("Save"))
        {
            if (_currentPath != null)
            {
                _currentMaterial.Name = name; // Update internal name from UI before saving
                SaveMaterial();
            }
            else
                Console.WriteLine("[MaterialEditor] No file path set. Use 'File > Save As'.");
        }
        ImGui.SameLine();

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
            _previewRenderer.UpdateMaterial(_currentMaterial); // Live update
            MaterialChanged?.Invoke(_currentPath, _currentMaterial, _sceneMaterialIndex);
        }
    }
    
    public void Dispose()
    {
        _previewRenderer.Dispose();
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
        
        ImGui.BeginGroup(); // Wrap in group for robust drag target
        if (texHandle != IntPtr.Zero)
        {
            // Image Button
            ImGui.ImageButton(label + "_btn", texHandle, size);
        }
        else
        {
            // Placeholder Button
            string btnText = string.IsNullOrEmpty(path) ? "None" : "Loading...";
            ImGui.Button(btnText, size);
        }
        ImGui.EndGroup();

        // Drag Drop Target
        if (ImGui.BeginDragDropTarget())
        {
            unsafe {
                var payload = ImGui.AcceptDragDropPayload("TEXTURE_ASSET");
                if (payload.NativePtr != null)
                {
                    string droppedPath = DragDropPayload.CurrentAssetPath;
                    if (!string.IsNullOrEmpty(droppedPath))
                    {
                        string ext = Path.GetExtension(droppedPath).ToLower();
                        if (ext == ".wltex" || ext == ".png" || ext == ".jpg" || ext == ".tga")
                        {
                            path = droppedPath;
                            _dirty = true;
                        }
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
                
                if (ImGui.MenuItem("Save As..."))
                {
                    _saveAsName = string.IsNullOrEmpty(_currentPath) ? "NewMaterial" : Path.GetFileNameWithoutExtension(_currentPath);
                    _showSaveAs = true;
                }
                
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }
    }
}
