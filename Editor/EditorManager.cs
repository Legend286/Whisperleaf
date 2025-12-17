using System;
using System.IO;
using System.Numerics;
using System.Text;
using ImGuiNET;
using ImGuizmoNET;
using ImPlotNET;
using Veldrid;
using Veldrid.Sdl2;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Editor.Windows;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Input;
using Whisperleaf.Platform;

namespace Whisperleaf.Editor;

/// <summary>
/// Main editor manager - handles all editor windows and ImGui integration
/// </summary>
public class EditorManager : IDisposable {
    private readonly GraphicsDevice _gd;
    private readonly ImGuiController _imguiController;
    private readonly List<EditorWindow> _windows = new();

    // Editor windows
    private readonly AssetBrowserWindow _assetBrowser;
    private readonly SceneInspectorWindow _sceneInspector;
    private readonly ImportWizardWindow _importWizard;
    private readonly FileDialogWindow _fileDialog;

    private SceneAsset? _currentScene;

    public event Action<SceneAsset, bool>? SceneRequested;

    public event Action<SceneNode?>? SceneNodeSelected;

    public event Action<OPERATION>? GizmoOperationChanged;

    public OPERATION GizmoOperation { get; private set; } = OPERATION.TRANSLATE;

    public bool ShowBVH { get; set; }

    public bool ShowSelection { get; set; } = true;

    public EditorManager(GraphicsDevice gd, Sdl2Window window) {
        _gd = gd;

        _imguiController = new ImGuiController(
            gd,
            gd.MainSwapchain.Framebuffer.OutputDescription,
            window.Width,
            window.Height);

        // Create editor windows
        _assetBrowser = new AssetBrowserWindow();
        _sceneInspector = new SceneInspectorWindow();
        _importWizard = new ImportWizardWindow();
        _fileDialog = new FileDialogWindow();

        _windows.Add(_assetBrowser);
        _windows.Add(_sceneInspector);
        _windows.Add(_importWizard);
        _windows.Add(_fileDialog);

        // Wire up events
        _assetBrowser.OnSceneSelected += OnSceneLoaded;
        _importWizard.OnImportComplete += OnImportComplete;
        window.DragDrop += OnWindowDragDrop;
        _sceneInspector.NodeSelected += node => SceneNodeSelected?.Invoke(node);
        _sceneInspector.GizmoOperationChanged += OnGizmoOperationChanged;
        GizmoOperation = _sceneInspector.CurrentOperation;
    }

    public void Update(float deltaTime, InputSnapshot snapshot) {
        _imguiController.Update(deltaTime, snapshot);
        
        // Shortcuts
        if (InputManager.IsKeyDown(Key.ControlLeft) || InputManager.IsKeyDown(Key.ControlRight))
        {
            if (InputManager.WasKeyPressed(Key.S))
            {
                if (_currentScene != null)
                {
                    if (!string.IsNullOrEmpty(_currentScene.ScenePath))
                    {
                        _currentScene.Save(_currentScene.ScenePath);
                    }
                    else
                    {
                        _saveSceneName = string.IsNullOrWhiteSpace(_currentScene.Name) ? "New Scene" : _currentScene.Name;
                        _saveSceneWindowOpen = true;
                    }
                }
            }
        }

        // Main menu bar
        DrawMenuBar();

        // Dockspace
        SetupDockspace();
        
        // Draw all windows
        foreach (var window in _windows) {
            window.Draw();
        }
        DrawSaveSceneWindow();
    }

    public void UpdateStats(RenderStats stats) {
        _sceneInspector.Stats = stats;
    }

    public void Render(CommandList cl) {
        _imguiController.Render(_gd, cl);
    }

    private void OnGizmoOperationChanged(OPERATION operation) {
        if (GizmoOperation == operation) {
            return;
        }


        GizmoOperation = operation;
        GizmoOperationChanged?.Invoke(operation);
    }

    public void WindowResized(int width, int height) {
        _imguiController.WindowResized(width, height);
    }
    private bool _saveSceneWindowOpen;
    private string _saveSceneName = "New Scene";
    private bool _sceneNameBlocked;

    private void DrawMenuBar() {
        if (ImGui.BeginMainMenuBar()) {
            if (ImGui.BeginMenu("File")) {
                if (ImGui.MenuItem("New Scene")) {
                    var scene = new SceneAsset { Name = "New Scene" };
                    OnImportComplete(scene);
                }


                ImGui.Separator();

                if (_currentScene != null)
                {
                    if (ImGui.MenuItem("Save"))
                    {
                        if (!string.IsNullOrEmpty(_currentScene.ScenePath))
                        {
                            _currentScene.Save(_currentScene.ScenePath);
                        }
                        else
                        {
                            _saveSceneName = string.IsNullOrWhiteSpace(_currentScene.Name) ? "New Scene" : _currentScene.Name;
                            _saveSceneWindowOpen = true;
                        }
                    }

                    if (ImGui.MenuItem("Save As..."))
                    {
                        _saveSceneName = string.IsNullOrWhiteSpace(_currentScene.Name) ? "New Scene" : _currentScene.Name;
                        _saveSceneWindowOpen = true;
                    }
                }


                if (ImGui.MenuItem("Import Model...")) {
                    OpenImportDialog();
                }


                ImGui.Separator();

                if (ImGui.MenuItem("Exit")) {
                    Window.Instance.SdlWindow.Close();
                }


                ImGui.EndMenu();
            }


            if (ImGui.BeginMenu("Create")) {
                if (ImGui.MenuItem("Point Light")) CreateLight(0);
                if (ImGui.MenuItem("Directional Light")) CreateLight(1);
                if (ImGui.MenuItem("Spot Light")) CreateLight(2);
                ImGui.EndMenu();
            }


            if (ImGui.BeginMenu("Windows")) {
                bool assetBrowserOpen = _assetBrowser.IsOpen;
                bool sceneInspectorOpen = _sceneInspector.IsOpen;

                if (ImGui.MenuItem("Asset Browser", null, ref assetBrowserOpen))
                    _assetBrowser.IsOpen = assetBrowserOpen;

                if (ImGui.MenuItem("Scene Inspector", null, ref sceneInspectorOpen))
                    _sceneInspector.IsOpen = sceneInspectorOpen;

                ImGui.EndMenu();
            }


            if (ImGui.BeginMenu("View")) {
                bool showBvh = ShowBVH;
                bool showSel = ShowSelection;
                if (ImGui.MenuItem("Show BVH", null, ref showBvh)) ShowBVH = showBvh;
                if (ImGui.MenuItem("Show Selection", null, ref showSel)) ShowSelection = showSel;
                ImGui.EndMenu();
            }


            if (ImGui.BeginMenu("Help")) {
                if (ImGui.MenuItem("About")) {
                }


                ImGui.EndMenu();
            }


            ImGui.EndMainMenuBar();
        }
    }
    
    private void DrawSaveSceneWindow()
    {
        if (!_saveSceneWindowOpen)
            return;

        // Optional: first-time size
        ImGui.SetNextWindowSize(new Vector2(420, 0), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Save Scene", ref _saveSceneWindowOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Save current scene to Resources/Scenes/");
        ImGui.Separator();

        ImGui.InputText("Scene Name", ref _saveSceneName, 32);

        // sanitize AFTER input, but don't destroy user typing too aggressively
        string sanitized = SanitizeAlphanumeric(_saveSceneName);
        if (sanitized != _saveSceneName)
        {
            ImGui.TextDisabled($"Sanitized: {sanitized}");
        }

        string finalName = string.IsNullOrWhiteSpace(sanitized) ? "NewScene" : sanitized;
        string path = Path.Combine("Resources/Scenes", $"{finalName}.wlscene");

        bool blocked = File.Exists(path);

        ImGui.Spacing();
        ImGui.TextUnformatted("Path:");
        ImGui.SameLine();
        ImGui.TextDisabled(path);

        if (blocked)
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "A scene with this name already exists.");

        ImGui.Separator();

        if (ImGui.Button("Save") && !blocked)
        {
            Directory.CreateDirectory("Resources/Scenes"); // ensure folder exists
            _currentScene!.Save(path);
            _saveSceneWindowOpen = false;
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel"))
        {
            _saveSceneWindowOpen = false;
        }

        ImGui.End();
    }
    
    private void SetupDockspace() {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking;
        windowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse;
        windowFlags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        windowFlags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
        windowFlags |= ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0.0f, 0.0f));

        ImGui.Begin("DockSpaceWindow", windowFlags);
        ImGui.PopStyleVar(3);

        var dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, new System.Numerics.Vector2(0.0f, 0.0f), ImGuiDockNodeFlags.PassthruCentralNode);

        ImGui.End();
    }

    private void OpenImportDialog() {
        var startPath = "Resources/Models";
        var extensions = new[] { ".gltf", ".glb", ".fbx", ".obj" };

        _fileDialog.Open("Import Model", extensions, startPath, path =>
        {
            if (File.Exists(path)) {
                _importWizard.Open(path);
            }
        });
    }

    private static bool IsModelFile(string path) {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        return ext is ".gltf" or ".glb" or ".fbx" or ".obj";
    }

    private static bool IsSceneFile(string path) {
        return string.Equals(Path.GetExtension(path), ".wlscene", StringComparison.OrdinalIgnoreCase);
    }

    private void TryLoadSceneFromPath(string path) {
        try {
            if (!File.Exists(path)) {
                return;
            }


            var scene = SceneAsset.Load(path);
            OnSceneLoaded(scene, false);
        }
        catch (Exception ex) {
            Console.WriteLine($"Failed to load scene from drop: {ex.Message}");
        }
    }

    private void OnWindowDragDrop(DragDropEvent e) {
        var file = e.File;

        if (string.IsNullOrWhiteSpace(file)) {
            return;
        }


        if (IsSceneFile(file)) {
            TryLoadSceneFromPath(file);
        }
        else if (IsModelFile(file)) {
            _importWizard.Open(file);
        }
    }

    private void OnSceneLoaded(SceneAsset scene, bool additive) {
        if (additive && _currentScene != null) {
            // Wrap new scene roots for organizational clarity in Inspector
            var wrapper = new SceneNode { Name = scene.Name };
            wrapper.Children.AddRange(scene.RootNodes);
            scene.RootNodes = new List<SceneNode> { wrapper };

            // Notify Renderer (Passes unmodified indices)
            SceneRequested?.Invoke(scene, true);

            // Update local Inspector state (Merge)
            int matOffset = _currentScene.Materials.Count;
            _currentScene.Materials.AddRange(scene.Materials);

            // Update indices in the NEW scene hierarchy
            foreach (var node in wrapper.GetMeshNodes()) {
                if (node.Mesh != null) node.Mesh.MaterialIndex += matOffset;
            }


            _currentScene.RootNodes.Add(wrapper);

            // Update Metadata
            _currentScene.Metadata.TotalMeshCount += scene.Metadata.TotalMeshCount;
            _currentScene.Metadata.TotalVertexCount += scene.Metadata.TotalVertexCount;
            _currentScene.Metadata.TotalTriangleCount += scene.Metadata.TotalTriangleCount;
            _currentScene.Metadata.BoundsMin = Vector3.Min(_currentScene.Metadata.BoundsMin, scene.Metadata.BoundsMin);
            _currentScene.Metadata.BoundsMax = Vector3.Max(_currentScene.Metadata.BoundsMax, scene.Metadata.BoundsMax);
        }
        else {
            _currentScene = scene;
            _sceneInspector.SetScene(scene);
            SceneRequested?.Invoke(scene, false);
        }
    }

    private void OnImportComplete(SceneAsset scene) {
        _currentScene = scene;
        _sceneInspector.SetScene(scene);
        _assetBrowser.IsOpen = true; // Refresh browser
        SceneRequested?.Invoke(scene, false);
    }

    private void CreateLight(int type) {
        if (_currentScene == null) {
            _currentScene = new SceneAsset { Name = "Untitled Scene" };
            _sceneInspector.SetScene(_currentScene);
        }


        var light = new SceneLight {
            Type = type,
            Color = Vector3.One,
            Intensity = type == 1 ? 5.0f : 20.0f,
            Range = 20.0f
        };

        var node = new SceneNode {
            Name = type switch { 0 => "Point Light", 1 => "Directional Light", 2 => "Spot Light", _ => "Light" },
            Light = light,
            LocalTransform = Matrix4x4.CreateTranslation(0, 2, 0)
        };

        // Add to persistent scene hierarchy (for Inspector)
        _currentScene.RootNodes.Add(node);

        // Create a temporary scene delta for the renderer (Additive)
        var deltaScene = new SceneAsset();
        deltaScene.RootNodes.Add(node);

        // Refresh renderer with just the new light
        SceneRequested?.Invoke(deltaScene, true);
    }

    public void Dispose() {
        _imguiController?.Dispose();
    }

    static string SanitizeAlphanumeric(string input) {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);

        foreach (char c in input) {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }


        return sb.ToString();
    }
}