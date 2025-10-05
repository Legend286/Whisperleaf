using System;
using System.IO;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Editor.Windows;

namespace Whisperleaf.Editor;

/// <summary>
/// Main editor manager - handles all editor windows and ImGui integration
/// </summary>
public class EditorManager : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ImGuiController _imguiController;
    private readonly List<EditorWindow> _windows = new();

    // Editor windows
    private readonly AssetBrowserWindow _assetBrowser;
    private readonly SceneInspectorWindow _sceneInspector;
    private readonly ImportWizardWindow _importWizard;
    private readonly FileDialogWindow _fileDialog;

    private SceneAsset? _currentScene;

    public event Action<SceneAsset>? SceneRequested;

    public EditorManager(GraphicsDevice gd, Sdl2Window window)
    {
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
    }

    public void Update(float deltaTime, InputSnapshot snapshot)
    {
        _imguiController.Update(deltaTime, snapshot);

        // Main menu bar
        DrawMenuBar();

        // Dockspace
        SetupDockspace();

        // Draw all windows
        foreach (var window in _windows)
        {
            window.Draw();
        }
    }

    public void Render(CommandList cl)
    {
        cl.Begin();
        cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
        _imguiController.Render(_gd, cl);
        cl.End();
        _gd.SubmitCommands(cl);
    }

    public void WindowResized(int width, int height)
    {
        _imguiController.WindowResized(width, height);
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Import Model..."))
                {
                    OpenImportDialog();
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Exit"))
                {
                    // TODO: Exit application
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Windows"))
            {
                bool assetBrowserOpen = _assetBrowser.IsOpen;
                bool sceneInspectorOpen = _sceneInspector.IsOpen;

                if (ImGui.MenuItem("Asset Browser", null, ref assetBrowserOpen))
                    _assetBrowser.IsOpen = assetBrowserOpen;
                if (ImGui.MenuItem("Scene Inspector", null, ref sceneInspectorOpen))
                    _sceneInspector.IsOpen = sceneInspectorOpen;

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("About"))
                {
                    // TODO: About dialog
                }

                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }
    }

    private void SetupDockspace()
    {
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

    private void OpenImportDialog()
    {
        var startPath = "Resources/Models";
        var extensions = new[] { ".gltf", ".glb", ".fbx", ".obj" };

        _fileDialog.Open("Import Model", extensions, startPath, path =>
        {
            if (File.Exists(path))
            {
                _importWizard.Open(path);
            }
        });
    }

    private static bool IsModelFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".gltf" or ".glb" or ".fbx" or ".obj";
    }

    private static bool IsSceneFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".wlscene", StringComparison.OrdinalIgnoreCase);
    }

    private void TryLoadSceneFromPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var scene = SceneAsset.Load(path);
            OnSceneLoaded(scene);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load scene from drop: {ex.Message}");
        }
    }

    private void OnWindowDragDrop(DragDropEvent e)
    {
        var file = e.File;
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        if (IsSceneFile(file))
        {
            TryLoadSceneFromPath(file);
        }
        else if (IsModelFile(file))
        {
            _importWizard.Open(file);
        }
    }

    private void OnSceneLoaded(SceneAsset scene)
    {
        _currentScene = scene;
        _sceneInspector.SetScene(scene);
        SceneRequested?.Invoke(scene);
    }

    private void OnImportComplete(SceneAsset scene)
    {
        _currentScene = scene;
        _sceneInspector.SetScene(scene);
        _assetBrowser.IsOpen = true; // Refresh browser
        SceneRequested?.Invoke(scene);
    }

    public void Dispose()
    {
        _imguiController?.Dispose();
    }
}
