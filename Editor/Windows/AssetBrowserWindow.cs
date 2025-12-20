using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;

namespace Whisperleaf.Editor.Windows;

public class AssetBrowserWindow : EditorWindow
{
    private readonly ThumbnailGenerator _thumbs;
    private string _currentPath;
    private List<string> _directories = new();
    private List<string> _files = new();
    
    private float _thumbnailSize = 96.0f;
    private float _padding = 8.0f;
    
    public event Action<SceneAsset, bool>? OnSceneSelected; // Legacy support

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
        ImGui.Text("Path:");
        ImGui.SameLine();
        
        // Split path relative to cache root for cleaner display
        var relPath = Path.GetRelativePath(AssetCache.CacheRoot, _currentPath);
        if (relPath == ".") relPath = "Root";
        
        if (ImGui.Button("Root"))
        {
            _currentPath = AssetCache.CacheRoot;
            Refresh();
        }
        
        if (relPath != "Root")
        {
            ImGui.SameLine();
            ImGui.Text("/");
            ImGui.SameLine();
            ImGui.Text(relPath);
            
            ImGui.SameLine();
            if (ImGui.Button("Up"))
            {
                var parent = Directory.GetParent(_currentPath);
                if (parent != null && parent.FullName.StartsWith(Path.GetFullPath(AssetCache.CacheRoot)))
                {
                    _currentPath = parent.FullName;
                    Refresh();
                }
            }
        }
        
        ImGui.Separator();

        // Layout: Left Panel (Tree), Right Panel (Grid)
        // For simplicity, just doing grid for now as tree logic is complex with arbitrary depth
        
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
                
                if (!isMesh && !isTex) continue;

                ImGui.TableNextColumn();
                string fileName = Path.GetFileNameWithoutExtension(file);

                ImGui.PushID(file);
                
                // Get Thumbnail
                IntPtr thumbId = _thumbs.GetThumbnail(file, isTex ? AssetType.Texture : AssetType.Mesh);
                
                if (thumbId != IntPtr.Zero)
                {
                    ImGui.ImageButton(file, thumbId, new Vector2(_thumbnailSize, _thumbnailSize));
                }
                else
                {
                    ImGui.Button($"{(isMesh ? "🧊" : "🎨")}\n{fileName}", new Vector2(_thumbnailSize, _thumbnailSize));
                }

                // Drag Source
                if (isMesh) // Only models allow drag drop currently
                {
                    if (ImGui.BeginDragDropSource())
                    {
                        DragDropPayload.CurrentAssetPath = file;
                        ImGui.Text(fileName);
                        ImGui.SetDragDropPayload("ASSET_BROWSER_ITEM", IntPtr.Zero, 0);
                        ImGui.EndDragDropSource();
                    }
                }

                ImGui.TextWrapped(fileName);
                ImGui.PopID();
            }
            
            ImGui.EndTable();
        }

        if (nextPath != null)
        {
            _currentPath = nextPath;
            Refresh();
        }
    }
}

public static class DragDropPayload
{
    public static string CurrentAssetPath = "";
}
