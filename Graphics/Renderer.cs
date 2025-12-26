using ImGuiNET;
using ImGuizmoNET;
using System.Numerics;
using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Editor;
using Whisperleaf.Editor.Windows;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.Immediate;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;
using Whisperleaf.Graphics.Shadows;
using Whisperleaf.Input;
using Whisperleaf.Physics;
using Whisperleaf.Platform;

namespace Whisperleaf.Graphics;

public class Renderer
{
    private readonly Window _window;
    private readonly CommandList _cl;
    private readonly List<IRenderPass> _passes = new();
    private readonly EditorManager _editorManager;
    private readonly GltfPass _scenePass;
    private readonly DepthPass _depthPass;
    private readonly ImmediateRenderer _immediateRenderer;
    public ShadowAtlas ShadowAtlas { get; }
    private readonly ShadowPass _shadowPass;
    public CsmAtlas CsmAtlas { get; }
    private readonly CsmPass _csmPass;
    private readonly CsmUniformBuffer _csmUniformBuffer;
    public ResourceLayout CsmLayout => _csmUniformBuffer.Layout;
    public ResourceSet CsmResourceSet => _csmUniformBuffer.ResourceSet;
    private readonly SkyboxPass _skyboxPass;
    private readonly BloomPass _bloomPass;

    // Game View Resources
    private Framebuffer? _viewFramebuffer;
    private Framebuffer? _finalViewFramebuffer;
    private Texture? _viewTexture;
    private Texture? _finalViewTexture;
    private TextureView? _viewTextureView;
    private Texture? _viewDepthTexture;
    private readonly ViewportWindow _viewportWindow;
    public uint ViewportWidth { get; private set; }
    public uint ViewportHeight { get; private set; }

    public bool ShowBVH { get; set; }
    public bool ShowDynamicBVH { get; set; }
    public bool ShowSelectionBounds { get; set; } = true;
    public bool ShowLightHeatmap { get; set; }
    public bool EnableShadows { get; set; } = true;

    // Post Process Params
    public bool BloomEnabled { get => _bloomPass.Enabled; set => _bloomPass.Enabled = value; }
    public float BloomThreshold { get => _bloomPass.Threshold; set => _bloomPass.Threshold = value; }
    public float BloomIntensity { get => _bloomPass.Intensity; set => _bloomPass.Intensity = value; }
    public float Exposure { get => _bloomPass.Exposure; set => _bloomPass.Exposure = value; }

    public SceneNode? SelectedNode => _selectedNode;
    public bool IsManipulating => ImGuizmo.IsUsing();

    public Camera MainCamera => _viewportWindow.Camera;
    
    public PhysicsThread Physics { get; private set; }

    private SceneNode? _selectedNode;
    private OPERATION _gizmoOperation;
    private bool _wasUsingGizmo;
    private bool _sunActiveThisFrame;
    private bool _resizeRequested;
    private uint _pendingWidth, _pendingHeight;

    public Renderer(Window window)
    {
        _window = window;
        _cl = _window.graphicsDevice.ResourceFactory.CreateCommandList();
        PbrLayout.Initialize(_window.graphicsDevice);
        
        Physics = new PhysicsThread();
        
        ShadowAtlas = new ShadowAtlas(_window.graphicsDevice);
        CsmAtlas = new CsmAtlas(_window.graphicsDevice);
        _csmUniformBuffer = new CsmUniformBuffer(_window.graphicsDevice, CsmAtlas.TextureView, CsmAtlas.ShadowSampler);
        
        var hdrOutputDesc = new OutputDescription(
            new OutputAttachmentDescription(PixelFormat.D32_Float_S8_UInt),
            new OutputAttachmentDescription(PixelFormat.R16_G16_B16_A16_Float));

        var ldrOutputDesc = new OutputDescription(
            null,
            new OutputAttachmentDescription(_window.graphicsDevice.MainSwapchain.Framebuffer.OutputDescription.ColorAttachments[0].Format));

        _scenePass = new GltfPass(_window.graphicsDevice, ShadowAtlas.ResourceLayout, _csmUniformBuffer.Layout, hdrOutputDesc);
        _scenePass.CsmResourceSet = _csmUniformBuffer.ResourceSet;
        _scenePass.ShadowAtlas = ShadowAtlas;
        _shadowPass = new ShadowPass(_window.graphicsDevice);
        _csmPass = new CsmPass(_window.graphicsDevice);
        _skyboxPass = new SkyboxPass(_window.graphicsDevice, _scenePass.CameraBuffer, hdrOutputDesc);
        _bloomPass = new BloomPass(_window.graphicsDevice, ldrOutputDesc);

        _immediateRenderer = new ImmediateRenderer(_window.graphicsDevice, hdrOutputDesc);

        _passes.Add(_scenePass);
        
        _editorManager = new EditorManager(_window.graphicsDevice, _window.SdlWindow, this);
        _editorManager.SceneNodeSelected += OnSceneNodeSelected;
        _editorManager.GizmoOperationChanged += operation => _gizmoOperation = operation;
        _editorManager.ResolveMaterialPath = idx => _scenePass.GetMaterial(idx)?.AssetPath;
        _editorManager.MaterialUpdated += (path, asset, index) => {
            if (path != null) _scenePass.UpdateMaterial(path, asset);
            else if (index >= 0) _scenePass.UpdateMaterial(index, asset);
        };
        _gizmoOperation = _editorManager.GizmoOperation;

        _editorManager.SceneRequested += OnSceneRequested;
        _window.WindowResized += _editorManager.WindowResized;

        // Initialize Game View
        ViewportWidth = (uint)_window.Width;
        ViewportHeight = (uint)_window.Height;
        if (ViewportWidth == 0) ViewportWidth = 1280;
        if (ViewportHeight == 0) ViewportHeight = 720;

        PerformResize(ViewportWidth, ViewportHeight);

        _depthPass = new DepthPass(_window.graphicsDevice, _viewFramebuffer!, _scenePass.CameraBuffer);

        _viewportWindow = new ViewportWindow(this, _window);
        _editorManager.AddWindow(_viewportWindow);
    }

    public void AddPass(IRenderPass pass) => _passes.Add(pass);

    public void AddLight(LightUniform light) => _scenePass.AddLight(light);

    public void AddCustomMesh(string name, MeshData data) => _scenePass.AddCustomMesh(name, data);

    public void ResizeViewport(uint width, uint height)
    {
        _pendingWidth = width;
        _pendingHeight = height;
        _resizeRequested = true;
    }

    private void PerformResize(uint width, uint height)
    {
        _window.graphicsDevice.WaitForIdle();
        ViewportWidth = width;
        ViewportHeight = height;

        _viewTexture?.Dispose();
        _viewTextureView?.Dispose();
        _finalViewTexture?.Dispose();
        _finalViewFramebuffer?.Dispose();
        _viewDepthTexture?.Dispose();
        _viewFramebuffer?.Dispose();

        var factory = _window.graphicsDevice.ResourceFactory;

        var swapchainFormat = _window.graphicsDevice.MainSwapchain.Framebuffer.OutputDescription.ColorAttachments[0].Format;
        var depthFormat = PixelFormat.D32_Float_S8_UInt;

        _viewTexture = factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1,
            PixelFormat.R16_G16_B16_A16_Float,
            TextureUsage.RenderTarget | TextureUsage.Sampled));
        
        _viewTextureView = factory.CreateTextureView(_viewTexture);

        _finalViewTexture = factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1,
            swapchainFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled));

        _viewDepthTexture = factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1,
            depthFormat,
            TextureUsage.DepthStencil));

        _viewFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(_viewDepthTexture, _viewTexture));
        _finalViewFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, _finalViewTexture));

        _bloomPass.Resize(width, height);
    }

    public IntPtr GetGameViewTextureId()
    {
        if (_finalViewTexture == null) return IntPtr.Zero;
        return _editorManager.GetTextureBinding(_finalViewTexture);
    }

    public void LoadScene(SceneAsset scene)
    {
        _scenePass.LoadScene(scene);
        _editorManager.SetScene(scene);
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

    public void UpdateNodeTransform(SceneNode node, Matrix4x4 transform) => _scenePass.ApplyWorldTransform(node, transform);

    public void Run(Action<float>? onUpdate = null)
    {
        while (_window.Exists)
        {
            if (_resizeRequested)
            {
                PerformResize(_pendingWidth, _pendingHeight);
                _resizeRequested = false;
            }

            Time.Update();

            var snapshot = _window.PumpEvents();
            if (!_window.Exists) break;

            if (_window.SdlWindow.WindowState == WindowState.Minimized || _window.Width == 0 || _window.Height == 0)
            {
                System.Threading.Thread.Sleep(10);
                continue;
            }

            onUpdate?.Invoke(Time.DeltaTime);

            ShowLightHeatmap = _editorManager.ShowLightHeatmap;
            EnableShadows = _editorManager.ShowShadows;

            _viewportWindow.Update(Time.DeltaTime);

            InputManager.Update(snapshot);
            _editorManager.Update(Time.DeltaTime, snapshot);

            var camera = _viewportWindow.Camera;
            var screenSize = new Vector2(ViewportWidth, ViewportHeight);
            int debugMode = ShowLightHeatmap ? 1 : 0;

            if (camera != null)
            {
                _scenePass.Update(camera);

                var lights = new List<SceneNode>(_scenePass.VisibleLights);
                ShadowAtlas.UpdateAllocations(lights, camera, _scenePass, EnableShadows);

                // Update CSM for sunlight
                Vector3 sunDir = new Vector3(0.2f, -1.0f, 0.3f); // Default
                bool sunFound = false;
                if (EnableShadows)
                {
                    foreach (var node in _scenePass.LightNodes)
                    {
                        if (node.Light?.Type == 1 && node.IsVisible && node.Light.CastShadows)
                        {
                            sunDir = Vector3.TransformNormal(new Vector3(0, 0, -1), _scenePass.TryGetWorldTransform(node, out var world) ? world : Matrix4x4.Identity);
                            sunFound = true;
                            break;
                        }
                    }
                }

                if (sunFound)
                {
                    CsmAtlas.UpdateCascades(camera, sunDir);
                    _csmUniformBuffer.Update(_window.graphicsDevice, CsmAtlas);
                    _skyboxPass.UpdateSun(-sunDir); 
                    _csmPass.PrepareRender(CsmAtlas, _scenePass);
                }
                _sunActiveThisFrame = sunFound;
            }

            // Update Transforms (GD Update - before CL)
            _scenePass.UpdateModelBuffer();

            // Prepare Resources (GD Update - before CL)
            _scenePass.PrepareResources(_window.graphicsDevice, camera, screenSize, debugMode);

            // Parallel Render Passes (Shadows, CSM, Depth) - RECORDING
            // Combine all commands into one list
            _cl.Begin();

            // 0. Render Material Preview (integrated into main CL)
            _editorManager.RenderPreview(_cl);

            if (camera != null)
            {
                // 1. Shadow Pass
                for (int i = 0; i < ShadowAtlas.GetLayerCount(); i++)
                {
                    _cl.SetFramebuffer(ShadowAtlas.GetFramebuffer(i));
                    _cl.ClearDepthStencil(1.0f);
                }
                _shadowPass.Render(_window.graphicsDevice, _cl, ShadowAtlas, _scenePass);

                // 2. CSM Pass
                if (_sunActiveThisFrame)
                {
                    _csmPass.Render(_cl, CsmAtlas, _scenePass);
                }

                // 3. Depth Pre-pass
                if (_viewFramebuffer != null)
                {
                    _cl.SetFramebuffer(_viewFramebuffer);
                    _cl.ClearDepthStencil(1.0f);
                    _depthPass.Render(_cl, _scenePass, camera);
                }

                // 4. Record Light Culling (Compute)
                _scenePass.RecordCulling(_cl);

                // 5. Main Pass
                _cl.SetFramebuffer(_viewFramebuffer);
                _cl.ClearColorTarget(0, RgbaFloat.Black);
                // Depth already rendered by DepthPass

                _scenePass.CsmResourceSet = _csmUniformBuffer.ResourceSet;

                foreach (var pass in _passes)
                {
                    pass.Render(_window.graphicsDevice, _cl, camera, screenSize, debugMode);
                }
                
                _skyboxPass.Render(_window.graphicsDevice, _cl, camera, screenSize, debugMode);

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

                _scenePass.DrawDebug(_immediateRenderer, _editorManager.ShowBVH, _editorManager.ShowDynamicBVH, _editorManager.ShowSelection);
                _immediateRenderer.Render(_cl, camera, screenSize);

                // 7. Post Processing
                if (_viewTextureView != null)
                {
                    _bloomPass.Render(_cl, _viewTextureView, _finalViewFramebuffer!);
                }
            }

            // 8. ImGui Pass
            _cl.SetFramebuffer(_window.graphicsDevice.MainSwapchain.Framebuffer);
            _cl.ClearColorTarget(0, RgbaFloat.Black);
            _editorManager.Render(_cl);

            _cl.End();
            _window.graphicsDevice.SubmitCommands(_cl);
            _window.graphicsDevice.WaitForIdle();

            _window.graphicsDevice.SwapBuffers(_window.graphicsDevice.MainSwapchain);
        }
    }

    private void OnSceneNodeSelected(SceneNode? node)
    {
        _selectedNode = node;
        _scenePass.SetSelectedNode(node);
    }

    public void DrawGizmo()
    {
        var camera = _viewportWindow.Camera;
        if (camera == null || _selectedNode == null)
        {
            return;
        }

        if (!_scenePass.TryGetWorldTransform(_selectedNode, out var gizmoTransform))
        {
            return;
        }

        var view = camera.ViewMatrix;
        var projection = camera.ProjectionMatrix;

        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetGizmoSizeClipSpace(0.05f);
        ImGuizmo.SetRect(_viewportWindow.Position.X, _viewportWindow.Position.Y, _viewportWindow.Size.X, _viewportWindow.Size.Y);

        if (_editorManager.SnapEnabled)
        {
            Vector3 snap = new Vector3(_editorManager.SnapValue);
            Matrix4x4 delta = Matrix4x4.Identity;
            ImGuizmo.Manipulate(ref view.M11, ref projection.M11, _gizmoOperation, MODE.WORLD, ref gizmoTransform.M11, ref delta.M11, ref snap.X);
        }
        else
        {
            ImGuizmo.Manipulate(ref view.M11, ref projection.M11, _gizmoOperation, MODE.WORLD, ref gizmoTransform.M11);
        }

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

    public void InstantiateAsset(string path) => _editorManager.InstantiateAsset(path);

    public void Dispose()
    {
        Physics.Dispose();
        _scenePass.Dispose();
        _depthPass.Dispose();
        _shadowPass.Dispose();
        _csmPass.Dispose();
        CsmAtlas.Dispose();
        _csmUniformBuffer.Dispose();
        ShadowAtlas.Dispose();
        _skyboxPass.Dispose();
        _bloomPass.Dispose();
        _viewTexture?.Dispose();
        _viewTextureView?.Dispose();
        _finalViewTexture?.Dispose();
        _finalViewFramebuffer?.Dispose();
        _viewDepthTexture?.Dispose();
        _viewFramebuffer?.Dispose();
        _immediateRenderer.Dispose();
        _editorManager.Dispose();
    }
}