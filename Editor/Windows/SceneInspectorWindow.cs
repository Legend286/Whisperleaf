using System;
using System.Numerics;
using ImGuiNET;
using ImGuizmoNET;
using Whisperleaf.AssetPipeline.Scene;

namespace Whisperleaf.Editor.Windows;

/// <summary>
/// Scene inspector showing hierarchical mesh structure
/// </summary>
public class SceneInspectorWindow : EditorWindow
{
    private SceneAsset? _currentScene;
    private SceneNode? _selectedNode;
    private OPERATION _gizmoOperation = OPERATION.TRANSLATE;

    public event Action<SceneNode?>? NodeSelected;
    public event Action<OPERATION>? GizmoOperationChanged;

    public OPERATION CurrentOperation => _gizmoOperation;

    public SceneInspectorWindow()
    {
        Title = "Scene Inspector";
    }

    public void SetScene(SceneAsset? scene)
    {
        _currentScene = scene;
        _selectedNode = null;
        NodeSelected?.Invoke(null);
    }

    protected override void OnDraw()
    {
        if (_currentScene == null)
        {
            ImGui.TextDisabled("No scene loaded");
            return;
        }

        // Scene info header
        if (ImGui.CollapsingHeader("Scene Info", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Name: {_currentScene.Name}");
            ImGui.Text($"Source: {Path.GetFileName(_currentScene.SourceFile)}");
            ImGui.Separator();

            var meta = _currentScene.Metadata;
            ImGui.Text($"Meshes: {meta.TotalMeshCount}");
            ImGui.Text($"Vertices: {meta.TotalVertexCount:N0}");
            ImGui.Text($"Triangles: {meta.TotalTriangleCount:N0}");
            ImGui.Separator();

            ImGui.Text($"Bounds: {meta.BoundsSize.X:F2} x {meta.BoundsSize.Y:F2} x {meta.BoundsSize.Z:F2}");
            ImGui.Text($"Scale: {meta.ScaleFactor}");
        }

        ImGui.Separator();

        // Hierarchy tree
        if (ImGui.CollapsingHeader("Hierarchy", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var rootNode in _currentScene.RootNodes)
            {
                DrawNodeTree(rootNode);
            }
        }

        // Selected node details
        if (_selectedNode != null)
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("Node Details", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawNodeDetails(_selectedNode);
            }
        }
    }

    private void SetOperation(OPERATION operation)
    {
        if (_gizmoOperation == operation)
        {
            return;
        }

        _gizmoOperation = operation;
        GizmoOperationChanged?.Invoke(operation);
    }

    private void DrawNodeTree(SceneNode node)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;

        if (_selectedNode == node)
            flags |= ImGuiTreeNodeFlags.Selected;

        if (node.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;

        // Node icon based on type
        string icon = node.Mesh != null ? "🔷" : "📁";
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

    private void DrawNodeDetails(SceneNode node)
    {
        ImGui.Text($"Name: {node.Name}");
        ImGui.Separator();

        // Transform
        if (ImGui.TreeNodeEx("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var pos = node.LocalTransform.Translation;
            ImGui.Text($"Position: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");

            var scale = new Vector3(
                node.LocalTransform.M11,
                node.LocalTransform.M22,
                node.LocalTransform.M33
            );
            ImGui.Text($"Scale: ({scale.X:F2}, {scale.Y:F2}, {scale.Z:F2})");

            ImGui.TreePop();
        }

        if (node.Mesh != null)
        {
            ImGui.Separator();
            ImGui.Text("Gizmo");

            bool translate = _gizmoOperation == OPERATION.TRANSLATE;
            bool rotate = _gizmoOperation == OPERATION.ROTATE;
            bool scale = _gizmoOperation == OPERATION.SCALE;

            if (ImGui.RadioButton("Translate", translate))
            {
                SetOperation(OPERATION.TRANSLATE);
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Rotate", rotate))
            {
                SetOperation(OPERATION.ROTATE);
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Scale", scale))
            {
                SetOperation(OPERATION.SCALE);
            }

            ImGui.Separator();
            if (ImGui.TreeNodeEx("Mesh", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text($"Vertices: {node.Mesh.VertexCount:N0}");
                ImGui.Text($"Indices: {node.Mesh.IndexCount:N0}");
                ImGui.Text($"Triangles: {node.Mesh.IndexCount / 3:N0}");
                ImGui.Separator();

                var bounds = node.Mesh.AABBMax - node.Mesh.AABBMin;
                ImGui.Text($"Bounds: ({bounds.X:F2}, {bounds.Y:F2}, {bounds.Z:F2})");
                ImGui.Text($"Material: {node.Mesh.MaterialIndex}");

                ImGui.TreePop();
            }
        }
    }
}
