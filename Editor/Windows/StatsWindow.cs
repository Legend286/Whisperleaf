using System;
using System.IO;
using ImGuiNET;
using Whisperleaf.AssetPipeline.Scene;

namespace Whisperleaf.Editor.Windows;

public class StatsWindow : EditorWindow
{
    private SceneAsset? _currentScene;
    public RenderStats Stats;

    public StatsWindow()
    {
        Title = "Statistics";
        IsOpen = true;
    }

    public void SetScene(SceneAsset? scene)
    {
        _currentScene = scene;
    }

    protected override void OnDraw()
    {
         if (_currentScene == null)
        {
            ImGui.TextDisabled("No scene loaded");
        }
        else
        {
            // Scene info header
            if (ImGui.CollapsingHeader("Scene Info", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text($"Name: {_currentScene.Name}");
                ImGui.Text($"Source: {Path.GetFileName(_currentScene.SourceFile)}");
                
                var meta = _currentScene.Metadata;
                ImGui.Text($"Bounds: {meta.BoundsSize.X:F2} x {meta.BoundsSize.Y:F2} x {meta.BoundsSize.Z:F2}");
                ImGui.Text($"Scale: {meta.ScaleFactor}");
            }
        }

        ImGui.Separator();

        if (ImGui.CollapsingHeader("Rendering", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Draw Calls: {Stats.DrawCalls}");
            ImGui.Text($"Instances: {Stats.RenderedInstances} / {Stats.TotalInstances}");

            float instReduction = 100.0f * (1.0f - (float)Stats.DrawCalls / Math.Max(1, Stats.RenderedInstances));
            ImGui.Text($"Batching Efficiency: {instReduction:F1}%");

            ImGui.Separator();
            ImGui.Text("Geometry:");
            ImGui.Text($"Triangles: {Stats.RenderedTriangles:N0} (Instanced)");
            ImGui.Text($"Vertices:  {Stats.RenderedVertices:N0} (Instanced)");

            ImGui.Separator();
            ImGui.Text("Culling (BVH):");
            ImGui.Text($"Nodes Visited: {Stats.NodesVisited:N0}");
            ImGui.Text($"Nodes Culled:  {Stats.NodesCulled:N0}");
            ImGui.Text($"Leafs Tested:  {Stats.LeafsTested:N0}");
            ImGui.Text($"Triangles Culled: {Stats.TrianglesCulled:N0}");
            
            ImGui.Separator();
            ImGui.Text("Source Assets (Unique):");
            ImGui.Text($"Materials: {Stats.UniqueMaterials}");
            ImGui.Text($"Meshes:    {Stats.SourceMeshes}");
            ImGui.Text($"Vertices:  {Stats.SourceVertices:N0}");
        }
    }
}
