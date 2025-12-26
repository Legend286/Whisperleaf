using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.AssetPipeline; // Fix MaterialAsset

namespace Whisperleaf.Editor.Windows;

public class AssetBrowserWindow : EditorWindow
{
    private readonly ThumbnailGenerator _thumbs;
    private string _currentPath;
    private List<string> _directories = new();
    private List<string> _files = new();
    
    private float _thumbnailSize = 96.0f;
    private float _padding = 8.0f;
    
    private bool _showNewFolderPopup;
    private string _newFolderName = "New Folder";
    
    private bool _showRenamePopup;
    private string _renameName = "";
    private string _renamePath = "";
    
    public event Action<SceneAsset, bool>? OnSceneSelected; // Legacy support
    public event Action<string>? OnMaterialSelected;
    public event Action<string>? OnImportRequested; // New event for Import

    public void NavigateTo(string assetPath)
    {
        string? dir = Path.GetDirectoryName(assetPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            // Ensure we are inside cache root
            string fullDir = Path.GetFullPath(dir);
            string fullRoot = Path.GetFullPath(AssetCache.CacheRoot);
            
            if (fullDir.StartsWith(fullRoot))
            {
                _currentPath = fullDir;
                Refresh();
            }
        }
    }

    public AssetBrowserWindow(ThumbnailGenerator thumbs)
    {
        Title = "Asset Browser";
        _thumbs = thumbs;
        _currentPath = AssetCache.CacheRoot;
        Refresh();
    }

    private void Refresh()
    {
        _directories.Clear();
        _files.Clear();

        if (Directory.Exists(_currentPath))
        {
            _directories.AddRange(Directory.GetDirectories(_currentPath));
            _files.AddRange(Directory.GetFiles(_currentPath));
        }
    }

    protected override void OnDraw()
    {
        // Breadcrumbs / Navigation
        if (ImGui.Button("⬆"))
        {
            var parent = Directory.GetParent(_currentPath);
            if (parent != null && parent.FullName.StartsWith(Path.GetFullPath(AssetCache.CacheRoot)))
            {
                _currentPath = parent.FullName;
                Refresh();
            }
        }
        ImGui.SameLine();
        ImGui.Text("Path:");
        ImGui.SameLine();

        // Breadcrumbs
        string relPath = Path.GetRelativePath(AssetCache.CacheRoot, _currentPath);
        if (relPath == ".") relPath = "";
        
        string[] parts = relPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        
        if (ImGui.Button("Root"))
        {
            _currentPath = AssetCache.CacheRoot;
            Refresh();
        }
        
        string accumPath = AssetCache.CacheRoot;
        foreach (var part in parts)
        {
            ImGui.SameLine();
            ImGui.Text("/");
            ImGui.SameLine();
            accumPath = Path.Combine(accumPath, part);
            if (ImGui.Button(part))
            {
                _currentPath = accumPath;
                Refresh();
            }
        }
        
        // Drag Drop Target for Main Panel (Move to current folder if dropped on background?)
        // Actually, dropping on background usually means "Move Here".
        // But the drop target needs to be on a window or item. 
        // We can put it on the table or a dummy filling the rest of space?
        // Let's rely on item-specific drops for now, and maybe window-wide drop.
        
        ImGui.Separator();

        // Layout: Left Panel (Tree), Right Panel (Grid)
        
        float cellWidth = _thumbnailSize + _padding * 2;
        float panelWidth = ImGui.GetContentRegionAvail().X;
        int columnCount = (int)(panelWidth / cellWidth);
        if (columnCount < 1) columnCount = 1;

        string? nextPath = null;

        if (ImGui.BeginTable("AssetGrid", columnCount))
        {
            // Directories
            foreach (var dir in _directories)
            {
                ImGui.TableNextColumn();
                string dirName = Path.GetFileName(dir);
                
                ImGui.PushID(dir);
                // Folder Icon (Text for now)
                ImGui.Button($"📁\n{dirName}", new Vector2(_thumbnailSize, _thumbnailSize));
                
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    ImGui.OpenPopup("dir_ctx");

                if (ImGui.BeginPopup("dir_ctx"))
                {
                    if (ImGui.MenuItem("Rename"))
                    {
                        _renamePath = dir;
                        _renameName = dirName;
                        _showRenamePopup = true;
                    }
                    ImGui.EndPopup();
                }
                
                // Drag Drop Target (Move into folder)
                if (ImGui.BeginDragDropTarget())
                {
                    HandleDropTarget(dir); // Move dropped item INTO this directory
                    ImGui.EndDragDropTarget();
                }
                
                // Drag Drop Source (Move folder itself)
                if (ImGui.BeginDragDropSource())
                {
                    DragDropPayload.CurrentAssetPath = dir;
                    unsafe
                    {
                        // Payload ID can be generic "FILE_SYSTEM_ITEM"
                        ImGui.SetDragDropPayload("FILE_SYSTEM_ITEM", IntPtr.Zero, 0); 
                    }
                    ImGui.Text(dirName);
                    ImGui.EndDragDropSource();
                }

                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    nextPath = dir;
                }
                ImGui.PopID();
            }

            // Files
            foreach (var file in _files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                bool isMesh = ext == ".wlmesh";
                bool isTex = ext == ".wltex";
                bool isMat = ext == ".wlmat";
                // Allow moving any file really?
                
                if (!isMesh && !isTex && !isMat) continue;

                ImGui.TableNextColumn();
                string fileName = Path.GetFileNameWithoutExtension(file);

                ImGui.PushID(file);
                
                // Get Thumbnail
                IntPtr thumbId = IntPtr.Zero;
                if (!isMat) thumbId = _thumbs.GetThumbnail(file, isTex ? AssetType.Texture : AssetType.Mesh);
                
                if (thumbId != IntPtr.Zero)
                {
                    ImGui.ImageButton(file, thumbId, new Vector2(_thumbnailSize, _thumbnailSize));
                }
                else
                {
                    string icon = isMesh ? "🧊" : (isTex ? "🎨" : "🔮");
                    ImGui.Button($"{icon}\n{fileName}", new Vector2(_thumbnailSize, _thumbnailSize));
                }
                
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    ImGui.OpenPopup("file_ctx");

                if (ImGui.BeginPopup("file_ctx"))
                {
                    if (ImGui.MenuItem("Rename"))
                    {
                        _renamePath = file;
                        _renameName = Path.GetFileName(file);
                        _showRenamePopup = true;
                    }
                    ImGui.EndPopup();
                }

                // Interaction
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    if (isMat) OnMaterialSelected?.Invoke(file);
                    else if (isMesh) 
                    {
                        DragDropPayload.CurrentAssetPath = file; // Fallback
                    }
                }

                // Drag Source
                if (ImGui.BeginDragDropSource())
                {
                    DragDropPayload.CurrentAssetPath = file;
                    unsafe
                    {
                        // Generic payload for internal moves + Specific for Inspector
                        ImGui.SetDragDropPayload("FILE_SYSTEM_ITEM", IntPtr.Zero, 0);
                        
                        // Also set specific payload for inspector compatibility if needed?
                        // Inspector expects "MODEL_ASSET" etc.
                        // ImGui only allows one payload? No, SetDragDropPayload sets THE payload.
                        // We can only set one. Inspector needs specific.
                        // Let's use specific IDs based on type for inspector, 
                        // BUT ensure our folder drop target accepts ALL of them.
                        
                        string payloadId = isTex ? "TEXTURE_ASSET" : (isMesh ? "MODEL_ASSET" : "MATERIAL_ASSET");
                        ImGui.SetDragDropPayload(payloadId, IntPtr.Zero, 0);
                    }
                    
                    ImGui.Text(fileName);
                    ImGui.EndDragDropSource();
                }

                ImGui.TextWrapped(fileName);
                ImGui.PopID();
            }
            
            ImGui.EndTable();
        }
        
        // Window-wide drop target (e.g. from external OS, or moving back to current dir? Moving back to current dir is no-op)
        // Unless we are dragging from a subfolder TO here? That usually requires dropping on breadcrumbs or ".."
        // Let's add drop target to "Up" button or Breadcrumb "Root".
        
        if (nextPath != null)
        {
            _currentPath = nextPath;
            Refresh();
        }

        // Context Menu for Background (Right Click on empty space)
        if (ImGui.BeginPopupContextWindow("AssetContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            if (ImGui.MenuItem("New Folder"))
            {
                _newFolderName = "New Folder";
                _showNewFolderPopup = true;
            }
            if (ImGui.MenuItem("New Material"))
            {
                CreateNewMaterial();
            }
            if (ImGui.MenuItem("Import..."))
            {
                OnImportRequested?.Invoke(_currentPath);
            }
            ImGui.EndPopup();
        }

        DrawPopups();
    }

    private void DrawPopups()
    {
        if (_showNewFolderPopup)
        {
            ImGui.OpenPopup("New Folder");
            _showNewFolderPopup = false;
        }

        if (ImGui.BeginPopupModal("New Folder", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("Name", ref _newFolderName, 64);
            
            if (ImGui.Button("Create", new Vector2(120, 0)))
            {
                if (!string.IsNullOrWhiteSpace(_newFolderName))
                {
                    try
                    {
                        string path = Path.Combine(_currentPath, _newFolderName);
                        if (!Directory.Exists(path))
                        {
                            Directory.CreateDirectory(path);
                            Refresh();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AssetBrowser] Failed to create folder: {ex.Message}");
                    }
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (_showRenamePopup)
        {
            ImGui.OpenPopup("Rename Asset");
            _showRenamePopup = false;
        }

        if (ImGui.BeginPopupModal("Rename Asset", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("New Name", ref _renameName, 64);
            
            if (ImGui.Button("Rename", new Vector2(120, 0)))
            {
                if (!string.IsNullOrWhiteSpace(_renameName) && File.Exists(_renamePath) || Directory.Exists(_renamePath))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(_renamePath)!;
                        string newPath = Path.Combine(dir, _renameName);
                        
                        if (_renamePath != newPath)
                        {
                            if (Directory.Exists(_renamePath))
                                Directory.Move(_renamePath, newPath);
                            else
                                File.Move(_renamePath, newPath);
                            Refresh();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AssetBrowser] Failed to rename: {ex.Message}");
                    }
                }
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

    private void CreateNewMaterial()
    {
        string baseName = "NewMaterial";
        string path = Path.Combine(_currentPath, baseName + ".wlmat");
        int i = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(_currentPath, $"{baseName}_{i++}.wlmat");
        }
        
        var mat = new MaterialAsset { Name = Path.GetFileNameWithoutExtension(path) };
        mat.Save(path);
        Refresh();
    }

    private void HandleDropTarget(string targetDir)
    {
        // Accept multiple types
        bool accepted = false;
        unsafe {
            if (ImGui.AcceptDragDropPayload("FILE_SYSTEM_ITEM").NativePtr != null) accepted = true;
            if (ImGui.AcceptDragDropPayload("MODEL_ASSET").NativePtr != null) accepted = true;
            if (ImGui.AcceptDragDropPayload("TEXTURE_ASSET").NativePtr != null) accepted = true;
            if (ImGui.AcceptDragDropPayload("MATERIAL_ASSET").NativePtr != null) accepted = true;
        }

        if (accepted)
        {
            string sourcePath = DragDropPayload.CurrentAssetPath;
            if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath) || Directory.Exists(sourcePath))
            {
                try 
                {
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(targetDir, fileName);
                    
                    if (sourcePath != destPath)
                    {
                        if (Directory.Exists(sourcePath))
                            Directory.Move(sourcePath, destPath);
                        else
                            File.Move(sourcePath, destPath);
                            
                        Refresh();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AssetBrowser] Move failed: {ex.Message}");
                }
            }
        }
    }

}

public static class DragDropPayload
{
    public static string CurrentAssetPath = "";
}
