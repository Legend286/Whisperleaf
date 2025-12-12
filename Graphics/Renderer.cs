using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using ImGuizmoNET;
using Veldrid;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Editor;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;
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
    private Camera? _camera;
    private CameraController? _cameraController;
    private SceneNode? _selectedNode;
    private OPERATION _gizmoOperation;
    public Renderer(Window window)
    {
        _window = window;
        _cl = _window.graphicsDevice.ResourceFactory.CreateCommandList();
        PbrLayout.Initialize(_window.graphicsDevice);
        _editorManager = new EditorManager(_window.graphicsDevice, _window.SdlWindow);
        _editorManager.SceneNodeSelected += OnSceneNodeSelected;
        _editorManager.GizmoOperationChanged += operation => _gizmoOperation = operation;
        _gizmoOperation = _editorManager.GizmoOperation;

        _scenePass = new GltfPass(_window.graphicsDevice);

        
        _scenePass.AddLight(new LightUniform(
                    position: new Vector3(-3,1,0),
                    range: 30.0f,
                    color: new Vector3(0.5f, 1.0f, 0.75f),
                    intensity: 20.0f, type: LightType.Spot, innerCone: 40, outerCone: 60, direction: new Vector3(-1,0,1)));
            
        


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
            
            _cameraController?.Update(Time.DeltaTime);
            var snapshot = _window.PumpEvents();
            InputManager.Update(snapshot);
            _editorManager.Update(Time.DeltaTime, snapshot);
            _cl.Begin();
            
            _cl.SetFramebuffer(_window.graphicsDevice.MainSwapchain.Framebuffer);
            _cl.ClearColorTarget(0, RgbaFloat.Black);
            _cl.ClearDepthStencil(1.0f);
            
            foreach (var pass in _passes)
            {
                pass.Render(_window.graphicsDevice, _cl, _camera);
            }

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

        if (ImGuizmo.IsUsing())
        {
            _scenePass.ApplyWorldTransform(_selectedNode, gizmoTransform);
        }
    }

    private void OnSceneRequested(SceneAsset scene)
    {
        try
        {
            _scenePass.LoadScene(scene);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Renderer failed to load scene: {ex.Message}");
        }
    }
}
