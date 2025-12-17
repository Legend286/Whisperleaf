using System;
using System.Numerics;
using ImGuiNET;
using ImGuizmoNET;
using Whisperleaf.AssetPipeline.Scene;

namespace Whisperleaf.Editor.Windows;

/// <summary>
/// Scene inspector showing hierarchical mesh structure
/// </summary>
public class SceneInspectorWindow : EditorWindow {
    private SceneAsset? _currentScene;
    private SceneNode? _selectedNode;
    private OPERATION _gizmoOperation = OPERATION.TRANSLATE;

    public RenderStats Stats;

    public event Action<SceneNode?>? NodeSelected;

    public event Action<OPERATION>? GizmoOperationChanged;

    public OPERATION CurrentOperation => _gizmoOperation;

    public SceneInspectorWindow() {
        Title = "Scene Inspector";
    }

    public void SetScene(SceneAsset? scene) {
        _currentScene = scene;
        _selectedNode = null;
        NodeSelected?.Invoke(null);
    }

    protected override void OnDraw() {
        if (_currentScene == null) {
            ImGui.TextDisabled("No scene loaded");

            return;
        }


        // Scene info header
        if (ImGui.CollapsingHeader("Scene Info", ImGuiTreeNodeFlags.DefaultOpen)) {
            ImGui.Text($"Name: {_currentScene.Name}");
            ImGui.Text($"Source: {Path.GetFileName(_currentScene.SourceFile)}");
            ImGui.Separator();

            // Rendering Stats
            ImGui.Text("Rendering Performance:");
            ImGui.Text($"  Draw Calls: {Stats.DrawCalls}");
            ImGui.Text($"  Instances: {Stats.RenderedInstances} / {Stats.TotalInstances}");

            float instReduction = 100.0f * (1.0f - (float)Stats.DrawCalls / Math.Max(1, Stats.RenderedInstances));
            ImGui.Text($"  Batching Efficiency: {instReduction:F1}%");

            ImGui.Separator();
            ImGui.Text("Culling (BVH):");
            ImGui.Text($"  Nodes Visited: {Stats.NodesVisited:N0}");
            ImGui.Text($"  Nodes Culled:  {Stats.NodesCulled:N0}");
            ImGui.Text($"  Leafs Tested:  {Stats.LeafsTested:N0}");
            ImGui.Text($"  Tris Culled:   {Stats.TrianglesCulled:N0}");

            ImGui.Separator();
            ImGui.Text("Geometry:");
            ImGui.Text($"  Triangles: {Stats.RenderedTriangles:N0} (Instanced)");
            ImGui.Text($"  Vertices:  {Stats.RenderedVertices:N0} (Instanced)");

            ImGui.Separator();
            ImGui.Text("Source Assets (Unique):");
            ImGui.Text($"  Materials: {Stats.UniqueMaterials}");
            ImGui.Text($"  Meshes:    {Stats.SourceMeshes}");
            ImGui.Text($"  Vertices:  {Stats.SourceVertices:N0}");
            // ImGui.Text($"  Triangles: {Stats.SourceTriangles:N0}"); // We didn't track source triangles, only indices/vertices. Indices/3 approx.

            ImGui.Separator();

            var meta = _currentScene.Metadata;
            // ImGui.Text($"Meshes: {meta.TotalMeshCount}"); // Metadata might be outdated vs runtime unique cache?
            // Keep metadata display as reference

            ImGui.Text($"Bounds: {meta.BoundsSize.X:F2} x {meta.BoundsSize.Y:F2} x {meta.BoundsSize.Z:F2}");
            ImGui.Text($"Scale: {meta.ScaleFactor}");
        }


        ImGui.Separator();

        // Hierarchy tree
        if (ImGui.CollapsingHeader("Hierarchy", ImGuiTreeNodeFlags.DefaultOpen)) {
            foreach (var rootNode in _currentScene.RootNodes) {
                DrawNodeTree(rootNode);
            }
        }


        // Selected node details
        if (_selectedNode != null) {
            ImGui.Separator();

            if (ImGui.CollapsingHeader("Node Details", ImGuiTreeNodeFlags.DefaultOpen)) {
                DrawNodeDetails(_selectedNode);
            }
        }
    }

    private void SetOperation(OPERATION operation) {
        if (_gizmoOperation == operation) {
            return;
        }


        _gizmoOperation = operation;
        GizmoOperationChanged?.Invoke(operation);
    }

    private void DrawNodeTree(SceneNode node) {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;

        if (_selectedNode == node)
            flags |= ImGuiTreeNodeFlags.Selected;

        if (node.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;

        // Node icon based on type
        string icon = node.Mesh != null ? "🔷" : "📁";
        bool nodeOpen = ImGui.TreeNodeEx($"{icon} {node.Name}###{node.GetHashCode()}", flags);

        // Selection
        if (ImGui.IsItemClicked()) {
            _selectedNode = node;
            NodeSelected?.Invoke(_selectedNode);
        }


        // Children
        if (nodeOpen) {
            foreach (var child in node.Children) {
                DrawNodeTree(child);
            }


            ImGui.TreePop();
        }
    }

    private void DrawNodeDetails(SceneNode node) {
        ImGui.Text($"Name: {node.Name}");
        ImGui.Separator();

        // Transform
        if (ImGui.TreeNodeEx("Transform", ImGuiTreeNodeFlags.DefaultOpen)) {
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


        ImGui.Separator();
        ImGui.Text("Gizmo");

        bool _translate = _gizmoOperation == OPERATION.TRANSLATE;
        bool _rotate = _gizmoOperation == OPERATION.ROTATE;
        bool _scale = _gizmoOperation == OPERATION.SCALE;

        if (ImGui.RadioButton("Translate", _translate)) {
            SetOperation(OPERATION.TRANSLATE);
        }


        ImGui.SameLine();

        if (ImGui.RadioButton("Rotate", _rotate)) {
            SetOperation(OPERATION.ROTATE);
        }


        ImGui.SameLine();

        if (ImGui.RadioButton("Scale", _scale)) {
            SetOperation(OPERATION.SCALE);
        }


        if (node.Mesh != null) {
            ImGui.Separator();

            if (ImGui.TreeNodeEx("Mesh", ImGuiTreeNodeFlags.DefaultOpen)) {
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


        if (node.Light != null) {
            ImGui.Separator();

            if (ImGui.TreeNodeEx("Light", ImGuiTreeNodeFlags.DefaultOpen)) {
                var light = node.Light;

                // Type
                string[] types = { "Point", "Directional", "Spot" };
                int type = light.Type;

                if (ImGui.Combo("Type", ref type, types, types.Length)) {
                    light.Type = type;
                }


                // Color
                Vector3 color = light.Color;

                if (ImGui.ColorEdit3("Color", ref color)) {
                    light.Color = color;
                }


                // Intensity
                float intensity = light.Intensity;

                if (ImGui.DragFloat("Intensity", ref intensity, 0.1f, 0.0f, 1000.0f)) {
                    light.Intensity = intensity;
                }


                if (type != 1) // Not directional
                {
                    // Range
                    float range = light.Range;

                    if (ImGui.DragFloat("Range", ref range, 0.5f, 0.0f, 1000.0f)) {
                        light.Range = range;
                    }
                }


                if (type == 2) // Spot
                {
                    // Cone angles (Radians -> Degrees for UI)
                    float innerDeg = light.InnerCone * 180.0f / MathF.PI;
                    float outerDeg = light.OuterCone * 180.0f / MathF.PI;

                    bool changed = false;
                    if (ImGui.DragFloat("Inner Angle", ref innerDeg, 1.0f, 0.0f, 179.0f)) changed = true;
                    if (ImGui.DragFloat("Outer Angle", ref outerDeg, 1.0f, 0.0f, 179.0f)) changed = true;

                    if (changed) {
                        light.InnerCone = innerDeg * MathF.PI / 180.0f;
                        light.OuterCone = outerDeg * MathF.PI / 180.0f;
                    }
                }


                ImGui.TreePop();
            }
        }
    }
}