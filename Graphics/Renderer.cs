using ImGuiNET;
using ImGuizmoNET;
using Veldrid;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Editor;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.Immediate;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;
using Whisperleaf.Graphics.Shadows;
using Whisperleaf.Input;
using Whisperleaf.Platform;

namespace Whisperleaf.Graphics;

public class Renderer
{
    private readonly Window _window;
    private readonly CommandList _cl;
    private readonly List<IRenderPass> _passes = new();
    private readonly EditorManager _editorManager;
    private readonly GltfPass _scenePass;
    private readonly ImmediateRenderer _immediateRenderer;
    private readonly ShadowAtlas _shadowAtlas;
    private readonly ShadowPass _shadowPass;
    
    public bool ShowBVH { get; set; }
    public bool ShowSelectionBounds { get; set; } = true;
    
    private Camera? _camera;
    private CameraController? _cameraController;
    private SceneNode? _selectedNode;
    private OPERATION _gizmoOperation;
    private bool _wasUsingGizmo;
    public Renderer(Window window)
    {
        _window = window;
        _cl = _window.graphicsDevice.ResourceFactory.CreateCommandList();
        PbrLayout.Initialize(_window.graphicsDevice);
        _editorManager = new EditorManager(_window.graphicsDevice, _window.SdlWindow);
        _editorManager.SceneNodeSelected += OnSceneNodeSelected;
        _editorManager.GizmoOperationChanged += operation => _gizmoOperation = operation;
        _gizmoOperation = _editorManager.GizmoOperation;

        _shadowAtlas = new ShadowAtlas(_window.graphicsDevice);
        _scenePass = new GltfPass(_window.graphicsDevice, _shadowAtlas.ResourceLayout);
        _scenePass.ShadowAtlas = _shadowAtlas;
        _shadowPass = new ShadowPass(_window.graphicsDevice);
        
        _immediateRenderer = new ImmediateRenderer(_window.graphicsDevice);

        _passes.Add(_scenePass);

        _editorManager.SceneRequested += OnSceneRequested;
        _window.WindowResized += _editorManager.WindowResized;
    }

    public void AddPass(IRenderPass pass) => _passes.Add(pass);
    
    public void AddLight(LightUniform light) => _scenePass.AddLight(light);

    public void SetCamera(Camera camera)
    {
        _camera = camera;
        _cameraController = new CameraController(_camera, _window);
    }

    public void LoadScene(SceneAsset scene)
    {
        _scenePass.LoadScene(scene);
    }

    public void LoadScene(string path)
    {
        try
        {
            _scenePass.LoadScene(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load scene '{path}': {ex.Message}");
        }
    }

    public void Run()
    {
        while (_window.Exists)
        {
            Time.Update();
            
            var snapshot = _window.PumpEvents();
            if (!_window.Exists) break;

            if (_window.SdlWindow.WindowState == WindowState.Minimized || _window.Width == 0 || _window.Height == 0)
            {
                System.Threading.Thread.Sleep(10);
                continue;
            }

            _cameraController?.Update(Time.DeltaTime);
            InputManager.Update(snapshot);
            _editorManager.Update(Time.DeltaTime, snapshot);
            
            if (_camera != null)
            {
                // Update Shadow Allocations
                // We convert IReadOnlyList to List or just pass generic? 
                // ShadowAtlas.UpdateAllocations takes List<SceneNode>.
                // _scenePass.LightNodes is IReadOnlyList.
                // We create a new list for now.
                var lights = new List<SceneNode>(_scenePass.LightNodes);
                _shadowAtlas.UpdateAllocations(lights, _camera);
            }

            _cl.Begin();
            
            // Clear Shadow Maps
            for (int i = 0; i < _shadowAtlas.GetLayerCount(); i++)
            {
                _cl.SetFramebuffer(_shadowAtlas.GetFramebuffer(i));
                _cl.ClearDepthStencil(1.0f);
            }

            // Render Shadows
            if (_camera != null)
            {
                _shadowPass.Render(_window.graphicsDevice, _cl, _shadowAtlas, _scenePass);
            }
            
            // Main Pass
            _cl.SetFramebuffer(_window.graphicsDevice.MainSwapchain.Framebuffer);
            _cl.ClearColorTarget(0, RgbaFloat.Black);
            _cl.ClearDepthStencil(1.0f);
            
            foreach (var pass in _passes)
            {
                pass.Render(_window.graphicsDevice, _cl, _camera);
            }

            var stats = new Editor.RenderStats
            {
                DrawCalls = _scenePass.DrawCalls,
                RenderedInstances = _scenePass.RenderedInstances,
                RenderedTriangles = _scenePass.RenderedTriangles,
                RenderedVertices = _scenePass.RenderedVertices,
                SourceMeshes = _scenePass.SourceMeshes,
                SourceVertices = _scenePass.SourceVertices,
                SourceTriangles = _scenePass.SourceIndices / 3,
                TotalInstances = _scenePass.TotalInstances,
                UniqueMaterials = _scenePass.UniqueMaterialCount,
                NodesVisited = _scenePass.CullingStats.NodesVisited,
                NodesCulled = _scenePass.CullingStats.NodesCulled,
                LeafsTested = _scenePass.CullingStats.LeafsTested,
                TrianglesCulled = _scenePass.TotalSceneTriangles - _scenePass.RenderedTriangles
            };
            _editorManager.UpdateStats(stats);

            _scenePass.DrawDebug(_immediateRenderer, _editorManager.ShowBVH, _editorManager.ShowSelection);
            if (_camera != null) _immediateRenderer.Render(_cl, _camera);

            HandleGizmo();
            _editorManager.Render(_cl);

            _cl.End();
            _window.graphicsDevice.SubmitCommands(_cl);
            
            _window.graphicsDevice.SwapBuffers(_window.graphicsDevice.MainSwapchain);
        }
    }

    private void OnSceneNodeSelected(SceneNode? node)
    {
        _selectedNode = node;
        _scenePass.SetSelectedNode(node);
    }

    private void HandleGizmo()
    {
        if (_camera == null || _selectedNode == null)
        {
            return;
        }

        if (!_scenePass.TryGetWorldTransform(_selectedNode, out var gizmoTransform))
        {
            return;
        }

        var view = _camera.ViewMatrix;
        var projection = _camera.ProjectionMatrix;

        ImGuizmo.SetOrthographic(false);
        var viewport = ImGui.GetMainViewport();
       
        ImGuizmo.SetRect(0,0,_window.Width, _window.Height);
        ImGuizmo.Manipulate(ref view.M11, ref projection.M11, _gizmoOperation, MODE.WORLD, ref gizmoTransform.M11);

        bool isUsing = ImGuizmo.IsUsing();
        _scenePass.IsGizmoActive = isUsing; // Update GltfPass with gizmo state
        
        if (isUsing)
        {
            _scenePass.ApplyWorldTransform(_selectedNode, gizmoTransform);
        }
        else if (_wasUsingGizmo)
        {
            _scenePass.RebuildBVH();
        }
        _wasUsingGizmo = isUsing;
    }

    private void OnSceneRequested(SceneAsset scene, bool additive)
    {
        try
        {
            _scenePass.LoadScene(scene, additive);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Renderer failed to load scene: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        _scenePass.Dispose();
        _shadowPass.Dispose();
        _shadowAtlas.Dispose();
        _immediateRenderer.Dispose();
        _editorManager.Dispose();
    }
}
