using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;

namespace Whisperleaf.Editor.Windows;

/// <summary>
/// Simple ImGui-based file picker for selecting import sources.
/// </summary>
public class FileDialogWindow : EditorWindow
{
    private readonly List<string> _directories = new();
    private readonly List<string> _files = new();
    private string _currentPath = Directory.GetCurrentDirectory();
    private string? _selectedFile;
    private Action<string>? _onFileSelected;
    private HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase);

    public FileDialogWindow()
    {
        Title = "File Browser";
        WindowFlags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse;
    }

    public void Open(string title, IEnumerable<string> extensions, string? startPath, Action<string> onFileSelected)
    {
        Title = title;
        _extensions = new HashSet<string>(extensions.Select(NormalizeExtension), StringComparer.OrdinalIgnoreCase);
        _onFileSelected = onFileSelected;
        _currentPath = ResolveStartPath(startPath);
        _selectedFile = null;
        RefreshEntries();
        IsOpen = true;
    }

    protected override void OnDraw()
    {
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(640, 400), ImGuiCond.FirstUseEver);

        ImGui.Text($"Current: {_currentPath}");

        if (ImGui.Button("Up") && TryNavigateUp(out string newPath))
        {
            _currentPath = newPath;
            RefreshEntries();
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
        {
            RefreshEntries();
        }

        ImGui.Separator();

        ImGui.BeginChild(
            "##file_list",
            new System.Numerics.Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 2),
            ImGuiChildFlags.None);

        string? navigateTo = null;

        foreach (var dir in _directories)
        {
            var label = $"📁 {Path.GetFileName(dir)}";
            if (string.IsNullOrEmpty(Path.GetFileName(dir)))
            {
                label = "📁 ..";
            }

            if (ImGui.Selectable(label, false, ImGuiSelectableFlags.AllowDoubleClick))
            {
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    navigateTo = dir;
                }
            }
        }

        foreach (var file in _files)
        {
            bool selected = _selectedFile == file;
            var label = $"📄 {Path.GetFileName(file)}";
            if (ImGui.Selectable(label, selected, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedFile = file;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    ConfirmSelection();
                }
            }
        }

        ImGui.EndChild();

        if (navigateTo != null)
        {
            NavigateTo(navigateTo);
        }

        ImGui.Separator();

        if (ImGui.Button("Open") && _selectedFile != null)
        {
            ConfirmSelection();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel"))
        {
            IsOpen = false;
        }
    }

    private string ResolveStartPath(string? startPath)
    {
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            try
            {
                var full = Path.GetFullPath(startPath);
                if (Directory.Exists(full))
                {
                    return full;
                }

                var parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    return parent;
                }
            }
            catch
            {
                // Fallback handled below.
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private void RefreshEntries()
    {
        _directories.Clear();
        _files.Clear();

        try
        {
            foreach (var dir in Directory.GetDirectories(_currentPath))
            {
                _directories.Add(dir);
            }
        }
        catch
        {
            // ignore directories we can't enumerate
        }

        try
        {
            foreach (var file in Directory.GetFiles(_currentPath))
            {
                if (_extensions.Count == 0 || _extensions.Contains(Path.GetExtension(file)))
                {
                    _files.Add(file);
                }
            }
        }
        catch
        {
            // ignore files we can't enumerate
        }

        _directories.Sort(StringComparer.OrdinalIgnoreCase);
        _files.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private bool TryNavigateUp(out string newPath)
    {
        try
        {
            var parent = Directory.GetParent(_currentPath);
            if (parent != null)
            {
                newPath = parent.FullName;
                return true;
            }
        }
        catch
        {
            // ignore
        }

        newPath = _currentPath;
        return false;
    }

    private void NavigateTo(string target)
    {
        try
        {
            if (Directory.Exists(target))
            {
                _currentPath = target;
                _selectedFile = null;
                RefreshEntries();
            }
        }
        catch
        {
            // ignore navigation failures
        }
    }

    private void ConfirmSelection()
    {
        if (_selectedFile == null || _onFileSelected == null)
        {
            return;
        }

        IsOpen = false;
        _onFileSelected?.Invoke(_selectedFile);
    }

    private static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
        {
            return string.Empty;
        }

        return ext.StartsWith('.') ? ext : $".{ext}";
    }
}
