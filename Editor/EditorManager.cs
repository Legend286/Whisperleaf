using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ImGuiNET;
using ImGuizmoNET;
using ImPlotNET;
using Veldrid;
using Veldrid.Sdl2;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Editor.Windows;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Input;
using Whisperleaf.Platform;
using TextureType = Whisperleaf.AssetPipeline.Cache.TextureType;

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
    private readonly SceneOutlinerWindow _sceneOutliner;
    private readonly InspectorWindow _inspector;
    private readonly StatsWindow _statsWindow;
    private readonly ImportWizardWindow _importWizard;
    private readonly FileDialogWindow _fileDialog;
    private readonly MaterialEditorWindow _materialEditor;

    private readonly ThumbnailGenerator _thumbnailGenerator;

    private SceneAsset? _currentScene;

    public event Action<SceneAsset, bool>? SceneRequested;

    public event Action<SceneNode?>? SceneNodeSelected;

    public event Action<OPERATION>? GizmoOperationChanged;
    
    public event Action? RequestRefresh;
    public event Action<string, MaterialAsset>? MaterialUpdated;
    public Func<int, string?>? ResolveMaterialPath;

    public OPERATION GizmoOperation { get; private set; } = OPERATION.TRANSLATE;

    public bool ShowBVH { get; set; }
    public bool ShowDynamicBVH { get; set; }
    public bool ShowSelection { get; set; } = true;
    public bool ShowLightHeatmap { get; set; }
    public bool ShowShadows { get; set; } = true;
    
    public bool SnapEnabled { get; set; }
    public float SnapValue { get; set; } = 0.5f;
    
    public EditorManager(GraphicsDevice gd, Sdl2Window window) {
        _gd = gd;
        
        // Ensure cache is up to date
        AssetCache.RebuildRegistry();

        _imguiController = new ImGuiController(
            gd,
            gd.MainSwapchain.Framebuffer.OutputDescription,
            window.Width,
            window.Height);
            
        _thumbnailGenerator = new ThumbnailGenerator(gd, this);

        // Create editor windows
        _assetBrowser = new AssetBrowserWindow(_thumbnailGenerator);
        _sceneOutliner = new SceneOutlinerWindow();
        _inspector = new InspectorWindow();
        _statsWindow = new StatsWindow();
        _importWizard = new ImportWizardWindow();
        _fileDialog = new FileDialogWindow();
        _materialEditor = new MaterialEditorWindow();

        _windows.Add(_assetBrowser);
        _windows.Add(_sceneOutliner);
        _windows.Add(_inspector);
        _windows.Add(_statsWindow);
        _windows.Add(_importWizard);
        _windows.Add(_fileDialog);
        _windows.Add(_materialEditor);

        // Wire up events
        _assetBrowser.OnSceneSelected += OnSceneLoaded;
        _assetBrowser.OnMaterialSelected += path => {
             _materialEditor.OpenMaterial(path);
             // Ensure window is visible (handled inside OpenMaterial, but maybe check menu state?)
        };
        _importWizard.OnImportComplete += OnImportComplete;
        window.DragDrop += OnWindowDragDrop;
        
        _sceneOutliner.NodeSelected += node => {
            _inspector.SetSelectedNode(node);
            SceneNodeSelected?.Invoke(node);
        };
        
        _inspector.GizmoOperationChanged += OnGizmoOperationChanged;
        _inspector.NodePropertyChanged += () => RequestRefresh?.Invoke();
        _inspector.MaterialDropped += (node, path) =>
        {
            if (_currentScene == null || node.Mesh == null) return;

            int foundIndex = -1;
            // Check if this material asset is already referenced in the scene
            for (int i = 0; i < _currentScene.Materials.Count; i++)
            {
                if (string.Equals(_currentScene.Materials[i].AssetPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    foundIndex = i;
                    break;
                }
            }

            if (foundIndex == -1)
            {
                // Load asset to get name, create reference
                try 
                {
                    var matAsset = MaterialAsset.Load(path);
                    var matRef = new MaterialReference
                    {
                        Name = matAsset.Name,
                        AssetPath = path
                    };
                    foundIndex = _currentScene.Materials.Count;
                    _currentScene.Materials.Add(matRef);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Editor] Failed to assign material: {ex.Message}");
                    return;
                }
            }

            node.Mesh.MaterialIndex = foundIndex;
            Console.WriteLine($"[Editor] Assigned material '{Path.GetFileName(path)}' to '{node.Name}'");
            
            // Trigger reload to update renderer
            SceneRequested?.Invoke(_currentScene, false);
        };
        _inspector.MaterialDoubleClicked += index => {
            if (ResolveMaterialPath != null)
            {
                var path = ResolveMaterialPath(index);
                if (!string.IsNullOrEmpty(path))
                {
                    _materialEditor.OpenMaterial(path);
                }
                else
                {
                    Console.WriteLine($"[Editor] Cannot open material {index}: No asset path (embedded material). Re-import model to generate .wlmat assets.");
                }
            }
        };
        
        _materialEditor.MaterialChanged += (path, asset) => MaterialUpdated?.Invoke(path, asset);
        
        GizmoOperation = _inspector.CurrentOperation;
    }

    public void SetScene(SceneAsset scene)
    {
        _currentScene = scene;
        _sceneOutliner.SetScene(scene);
        _statsWindow.SetScene(scene);
        _inspector.SetSelectedNode(null);
        _assetBrowser.IsOpen = true;
    }

    public void Update(float deltaTime, InputSnapshot snapshot) {
        _imguiController.Update(deltaTime, snapshot);
        _thumbnailGenerator.Update();
        
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
        else
        {
            // Transform Shortcuts (W/E/R)
            if (!ImGui.GetIO().WantTextInput)
            {
                if (InputManager.WasKeyPressed(Key.W)) SetGizmoOperation(OPERATION.TRANSLATE);
                if (InputManager.WasKeyPressed(Key.E)) SetGizmoOperation(OPERATION.ROTATE);
                if (InputManager.WasKeyPressed(Key.R)) SetGizmoOperation(OPERATION.SCALE);
            }
        }

        // Main menu bar
        DrawMenuBar();
        
        // Toolbar
        DrawToolbar();

        // Dockspace
        SetupDockspace();
        
        // Draw all windows
        foreach (var window in _windows) {
            window.Draw();
        }
        DrawSaveSceneWindow();
    }
    
    private void DrawToolbar()
    {
        var viewport = ImGui.GetMainViewport();
        
        ImGui.SetNextWindowPos(new Vector2(viewport.WorkPos.X, viewport.WorkPos.Y));
        ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, 36));
        
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking;
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        
        if (ImGui.Begin("Toolbar", flags))
        {
            if (ImGui.Button("T (W)", new Vector2(40, 0))) SetGizmoOperation(OPERATION.TRANSLATE);
            ImGui.SameLine();
            if (ImGui.Button("R (E)", new Vector2(40, 0))) SetGizmoOperation(OPERATION.ROTATE);
            ImGui.SameLine();
            if (ImGui.Button("S (R)", new Vector2(40, 0))) SetGizmoOperation(OPERATION.SCALE);
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            bool snap = SnapEnabled;
            if (ImGui.Checkbox("Snap", ref snap)) SnapEnabled = snap;
            
            if (SnapEnabled)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60);
                float val = SnapValue;
                if (ImGui.DragFloat("##snapval", ref val, 0.1f, 0.1f, 100.0f)) SnapValue = val;
            }
            
            ImGui.End();
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }
    
    private void SetGizmoOperation(OPERATION op)
    {
        if (GizmoOperation != op)
        {
            GizmoOperation = op;
            GizmoOperationChanged?.Invoke(op);
        }
    }

    public void OpenMaterialEditor(string path)
    {
        _materialEditor.OpenMaterial(path);
    }

    public void InstantiateAsset(string path)
    {
        if (_currentScene == null) 
        {
            _currentScene = new SceneAsset { Name = "Untitled" };
            OnImportComplete(_currentScene);
        }
        
        try 
        {
             var meshData = WlMeshFormat.Read(path, out string sourceHash);
             
             // Construct texture folder path
             string dir = Path.GetDirectoryName(path);
             string meshName = Path.GetFileNameWithoutExtension(path);
             string texDir = Path.Combine(dir, $"{meshName}_Textures");
             
             var matRef = new MaterialReference
             {
                 Name = $"{meshName}_Mat",
                 BaseColorFactor = Vector4.One,
                 RoughnessFactor = 0.8f,
                 MetallicFactor = 0.0f
             };
             
             // Attempt to find original material from scene file
             MaterialReference? originalMat = null;
             try 
             {
                 string cacheRoot = Path.GetFullPath(AssetCache.CacheRoot);
                 string fullPath = Path.GetFullPath(path);
                 if (fullPath.StartsWith(cacheRoot))
                 {
                     var relative = Path.GetRelativePath(cacheRoot, fullPath);
                     var parts = relative.Split(Path.DirectorySeparatorChar);
                     if (parts.Length >= 1)
                     {
                         string sceneName = parts[0];
                         // Search for .wlscene
                         string[] sceneFiles = Directory.GetFiles("Resources", $"{sceneName}.wlscene", SearchOption.AllDirectories);
                         if (sceneFiles.Length > 0)
                         {
                             var originalScene = SceneAsset.Load(sceneFiles[0]);
                             if (meshData.MaterialIndex >= 0 && meshData.MaterialIndex < originalScene.Materials.Count)
                             {
                                 originalMat = originalScene.Materials[meshData.MaterialIndex];
                             }
                         }
                     }
                 }
             }
             catch (Exception ex) { Console.WriteLine($"[InstantiateAsset] Failed to lookup scene material: {ex.Message}"); }

             if (originalMat != null)
             {
                 Console.WriteLine($"[InstantiateAsset] Using original material: {originalMat.Name}");
                 matRef.BaseColorHash = originalMat.BaseColorHash;
                 matRef.NormalHash = originalMat.NormalHash;
                 matRef.RMAHash = originalMat.RMAHash;
                 matRef.EmissiveHash = originalMat.EmissiveHash;
                 matRef.BaseColorFactor = originalMat.BaseColorFactor;
                 matRef.RoughnessFactor = originalMat.RoughnessFactor;
                 matRef.MetallicFactor = originalMat.MetallicFactor;
                 matRef.EmissiveFactor = originalMat.EmissiveFactor;
             }
             else
             {
                 Console.WriteLine($"[InstantiateAsset] Creating new material for {meshName}. Searching for textures in {texDir}");
                 
                 if (Directory.Exists(texDir))
                 {
                     var texFiles = Directory.GetFiles(texDir, "*.wltex");
                     Console.WriteLine($"[InstantiateAsset] Found {texFiles.Length} texture files.");
                     foreach (var texFile in texFiles)
                     {
                         try {
                             using var fs = File.OpenRead(texFile);
                             using var br = new BinaryReader(fs);
                             br.ReadUInt32(); // Magic
                             br.ReadUInt32(); // Version
                             br.ReadUInt32(); // W
                             br.ReadUInt32(); // H
                             var type = (TextureType)br.ReadUInt32();
                             br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
                             byte[] hashBytes = br.ReadBytes(32);
                             string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                             
                             AssetCache.RegisterExisting(hash, texFile, AssetType.Texture);
                             Console.WriteLine($"[InstantiateAsset] Registered texture: {Path.GetFileName(texFile)} ({type}) -> {hash}");
    
                             switch(type)
                             {
                                 case TextureType.BaseColor: matRef.BaseColorHash = hash; break;
                                 case TextureType.Normal: matRef.NormalHash = hash; break;
                                 case TextureType.RMA: matRef.RMAHash = hash; break;
                                 case TextureType.Emissive: matRef.EmissiveHash = hash; break;
                             }
                         }
                         catch (Exception e) { Console.WriteLine($"[InstantiateAsset] Error reading {texFile}: {e.Message}"); }
                     }
                 }
                 else 
                 {
                     Console.WriteLine($"[InstantiateAsset] No texture directory found at {texDir}");
                 }
             }

             // Add material to current scene
             int newMatIndex = _currentScene.Materials.Count;
             _currentScene.Materials.Add(matRef);
             
             var node = new SceneNode
             {
                 Name = meshName,
                 Mesh = new MeshReference
                 {
                     MeshHash = sourceHash,
                     MaterialIndex = newMatIndex, // Absolute
                     AABBMin = meshData.AABBMin,
                     AABBMax = meshData.AABBMax,
                     VertexCount = meshData.Vertices.Length / 12,
                     IndexCount = meshData.Indices.Length
                 },
                 LocalTransform = Matrix4x4.CreateTranslation(0, 0, 0),
                 IsStatic = true
             };
             
             _currentScene.RootNodes.Add(node);
             
             // Create delta for renderer
             var delta = new SceneAsset();
             delta.RootNodes.Add(node);
             delta.Materials.Add(matRef);
             
             // Temporary hack for additive load: Set MaterialIndex relative to delta (0)
             // GltfPass reads this during load to map to global index.
             node.Mesh.MaterialIndex = 0;
             
             SceneRequested?.Invoke(delta, true);
             
             // Restore absolute index for saving/inspector
             node.Mesh.MaterialIndex = newMatIndex;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to instantiate asset: {ex.Message}");
        }
    }

    public void AddWindow(EditorWindow window)
    {
        _windows.Add(window);
    }
    
    public IntPtr GetTextureBinding(Texture texture)
    {
        return _imguiController.GetOrCreateImGuiBinding(_gd.ResourceFactory, texture);
    }

    public void UpdateStats(RenderStats stats) {
        _statsWindow.Stats = stats;
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

                if (ImGui.MenuItem("Open Scene...")) {
                    _fileDialog.Open("Open Scene", new[] { ".wlscene" }, "Resources/Scenes", path => {
                        TryLoadSceneFromPath(path);
                    });
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
                bool outlinerOpen = _sceneOutliner.IsOpen;
                bool inspectorOpen = _inspector.IsOpen;
                bool statsOpen = _statsWindow.IsOpen;
                bool materialEditorOpen = _materialEditor.IsOpen;
                
                if (ImGui.MenuItem("Asset Browser", null, ref assetBrowserOpen))
                    _assetBrowser.IsOpen = assetBrowserOpen;

                if (ImGui.MenuItem("Scene Outliner", null, ref outlinerOpen))
                    _sceneOutliner.IsOpen = outlinerOpen;
                    
                if (ImGui.MenuItem("Inspector", null, ref inspectorOpen))
                    _inspector.IsOpen = inspectorOpen;

                if (ImGui.MenuItem("Statistics", null, ref statsOpen))
                    _statsWindow.IsOpen = statsOpen;

                if (ImGui.MenuItem("Material Editor", null, ref materialEditorOpen))
                    _materialEditor.IsOpen = materialEditorOpen;
                
                var viewport = _windows.OfType<ViewportWindow>().FirstOrDefault();
                if (viewport != null)
                {
                    bool viewportOpen = viewport.IsOpen;
                    if (ImGui.MenuItem("Game View", null, ref viewportOpen))
                        viewport.IsOpen = viewportOpen;
                }

                ImGui.EndMenu();
            }


            if (ImGui.BeginMenu("View")) {
                bool showBvh = ShowBVH;
                bool showDynBvh = ShowDynamicBVH;
                bool showSel = ShowSelection;
                bool showHeatmap = ShowLightHeatmap;
                bool showShadows = ShowShadows;
                if (ImGui.MenuItem("Show Static BVH", null, ref showBvh)) ShowBVH = showBvh;
                if (ImGui.MenuItem("Show Dynamic BVH", null, ref showDynBvh)) ShowDynamicBVH = showDynBvh;
                if (ImGui.MenuItem("Show Selection", null, ref showSel)) ShowSelection = showSel;
                if (ImGui.MenuItem("Show Light Heatmap", null, ref showHeatmap)) ShowLightHeatmap = showHeatmap;
                if (ImGui.MenuItem("Show Shadows", null, ref showShadows)) ShowShadows = showShadows;
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

        ImGui.SetNextWindowSize(new Vector2(420, 0), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Save Scene", ref _saveSceneWindowOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Save current scene to Resources/Scenes/");
        ImGui.Separator();

        ImGui.InputText("Scene Name", ref _saveSceneName, 32);

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
            Directory.CreateDirectory("Resources/Scenes");
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
        // Adjust for toolbar
        float toolbarHeight = 36.0f;
        
        ImGui.SetNextWindowPos(new Vector2(viewport.WorkPos.X, viewport.WorkPos.Y + toolbarHeight));
        ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, viewport.WorkSize.Y - toolbarHeight));
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoDocking;
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
            var wrapper = new SceneNode { Name = scene.Name };
            wrapper.Children.AddRange(scene.RootNodes);
            scene.RootNodes = new List<SceneNode> { wrapper };

            SceneRequested?.Invoke(scene, true);

            int matOffset = _currentScene.Materials.Count;
            _currentScene.Materials.AddRange(scene.Materials);

            foreach (var node in wrapper.GetMeshNodes()) {
                if (node.Mesh != null) node.Mesh.MaterialIndex += matOffset;
            }


            _currentScene.RootNodes.Add(wrapper);

            _currentScene.Metadata.TotalMeshCount += scene.Metadata.TotalMeshCount;
            _currentScene.Metadata.TotalVertexCount += scene.Metadata.TotalVertexCount;
            _currentScene.Metadata.TotalTriangleCount += scene.Metadata.TotalTriangleCount;
            _currentScene.Metadata.BoundsMin = Vector3.Min(_currentScene.Metadata.BoundsMin, scene.Metadata.BoundsMin);
            _currentScene.Metadata.BoundsMax = Vector3.Max(_currentScene.Metadata.BoundsMax, scene.Metadata.BoundsMax);
        }
        else {
            _currentScene = scene;
            _sceneOutliner.SetScene(scene);
            _statsWindow.SetScene(scene);
            SceneRequested?.Invoke(scene, false);
        }
    }

    private void OnImportComplete(SceneAsset scene) {
        _currentScene = scene;
        _sceneOutliner.SetScene(scene);
        _statsWindow.SetScene(scene);
        _assetBrowser.IsOpen = true; 
        SceneRequested?.Invoke(scene, false);
    }

    private void CreateLight(int type) {
        if (_currentScene == null) {
            _currentScene = new SceneAsset { Name = "Untitled Scene" };
            _sceneOutliner.SetScene(_currentScene);
            _statsWindow.SetScene(_currentScene);
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

        _currentScene.RootNodes.Add(node);

        var deltaScene = new SceneAsset();
        deltaScene.RootNodes.Add(node);

        SceneRequested?.Invoke(deltaScene, true);
    }

    public void Dispose() {
        _imguiController?.Dispose();
        _thumbnailGenerator?.Dispose();
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