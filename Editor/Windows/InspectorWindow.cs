using System;
using System.Numerics;
using ImGuiNET;
using ImGuizmoNET;
using Whisperleaf.AssetPipeline.Scene;

namespace Whisperleaf.Editor.Windows;

/// <summary>
/// Inspector window showing node details
/// </summary>
public class InspectorWindow : EditorWindow
{
    private SceneNode? _selectedNode;
    private OPERATION _gizmoOperation = OPERATION.TRANSLATE;

    public event Action<OPERATION>? GizmoOperationChanged;
    public event Action? NodePropertyChanged;

    public OPERATION CurrentOperation => _gizmoOperation;

    public InspectorWindow()
    {
        Title = "Inspector";
        IsOpen = true;
    }

    public void SetSelectedNode(SceneNode? node)
    {
        _selectedNode = node;
    }

    protected override void OnDraw()
    {
        if (_selectedNode == null)
        {
            ImGui.TextDisabled("No node selected");
            return;
        }

        DrawNodeDetails(_selectedNode);
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

    private void DrawNodeDetails(SceneNode node)
    {
        ImGui.Text($"Name: {node.Name}");
        ImGui.Separator();

        // IsVisible
        bool isVisible = node.IsVisible;
        if (ImGui.Checkbox("Visible", ref isVisible))
        {
            node.IsVisible = isVisible;
            NodePropertyChanged?.Invoke();
        }

        ImGui.SameLine();

        // IsStatic
        bool isStatic = node.IsStatic;
        if (ImGui.Checkbox("Static", ref isStatic))
        {
            node.IsStatic = isStatic;
            NodePropertyChanged?.Invoke();
        }
        
        ImGui.Separator();

        // Transform
        if (ImGui.TreeNodeEx("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var pos = node.LocalTransform.Translation;
            if (ImGui.DragFloat3("Position", ref pos, 0.1f))
            {
                 Matrix4x4.Decompose(node.LocalTransform, out var s, out var r, out _);
                 node.LocalTransform = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(pos);
            }

            ImGui.TreePop();
        }

        ImGui.Separator();
        ImGui.Text("Gizmo");

        bool _translate = _gizmoOperation == OPERATION.TRANSLATE;
        bool _rotate = _gizmoOperation == OPERATION.ROTATE;
        bool _scale = _gizmoOperation == OPERATION.SCALE;

        if (ImGui.RadioButton("Translate", _translate)) SetOperation(OPERATION.TRANSLATE);
        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate", _rotate)) SetOperation(OPERATION.ROTATE);
        ImGui.SameLine();
        if (ImGui.RadioButton("Scale", _scale)) SetOperation(OPERATION.SCALE);

        if (node.Mesh != null)
        {
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

        if (node.Light != null)
        {
            ImGui.Separator();

            if (ImGui.TreeNodeEx("Light", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var light = node.Light;

                // Type
                string[] types = { "Point", "Directional", "Spot" };
                int type = light.Type;

                if (ImGui.Combo("Type", ref type, types, types.Length))
                {
                    light.Type = type;
                    NodePropertyChanged?.Invoke();
                }

                // Color
                Vector3 color = light.Color;
                if (ImGui.ColorEdit3("Color", ref color))
                {
                    light.Color = color;
                }

                // Intensity
                float intensity = light.Intensity;
                if (ImGui.DragFloat("Intensity", ref intensity, 0.1f, 0.0f, 1000.0f))
                {
                    light.Intensity = intensity;
                }

                if (type != 1) // Not directional
                {
                    // Range
                    float range = light.Range;
                    if (ImGui.DragFloat("Range", ref range, 0.5f, 0.0f, 1000.0f))
                    {
                        light.Range = range;
                        NodePropertyChanged?.Invoke();
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

                    if (changed)
                    {
                        light.InnerCone = innerDeg * MathF.PI / 180.0f;
                        light.OuterCone = outerDeg * MathF.PI / 180.0f;
                        NodePropertyChanged?.Invoke();
                    }
                }

                ImGui.TreePop();
            }
        }
    }
}