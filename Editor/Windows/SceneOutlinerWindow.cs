using System;
using System.Collections.Generic;
using ImGuiNET;
using Whisperleaf.AssetPipeline.Scene;

namespace Whisperleaf.Editor.Windows;

/// <summary>
/// Scene outliner showing hierarchical mesh structure
/// </summary>
public class SceneOutlinerWindow : EditorWindow
{
    private SceneAsset? _currentScene;
    private SceneNode? _selectedNode;

    public event Action<SceneNode?>? NodeSelected;

    public SceneOutlinerWindow()
    {
        Title = "Scene Outliner";
        IsOpen = true;
    }

    public void SetScene(SceneAsset? scene)
    {
        _currentScene = scene;
        _selectedNode = null;
    }

    public void SetSelectedNode(SceneNode? node)
    {
        _selectedNode = node;
    }

    protected override void OnDraw()
    {
        if (_currentScene == null)
        {
            ImGui.TextDisabled("No scene loaded");
            return;
        }

        // Hierarchy tree
        foreach (var rootNode in _currentScene.RootNodes)
        {
            DrawNodeTree(rootNode);
        }
    }

    private void DrawNodeTree(SceneNode node)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;

        if (_selectedNode == node)
            flags |= ImGuiTreeNodeFlags.Selected;

        if (node.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;

        // Node icon based on type
        string icon = "📁";
        if (node.Light != null) icon = "💡";
        else if (node.Mesh != null) icon = "🔷";
        
        bool nodeOpen = ImGui.TreeNodeEx($"{icon} {node.Name}###{node.GetHashCode()}", flags);

        // Selection
        if (ImGui.IsItemClicked())
        {
            _selectedNode = node;
            NodeSelected?.Invoke(_selectedNode);
        }

        // Children
        if (nodeOpen)
        {
            foreach (var child in node.Children)
            {
                DrawNodeTree(child);
            }

            ImGui.TreePop();
        }
    }
}
