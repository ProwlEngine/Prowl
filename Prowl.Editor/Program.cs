using Prowl.Editor.Assets;
using Prowl.Editor.Drawers.NodeSystem;
using Prowl.Editor.Editor.Preferences;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.EditorWindows.CustomEditors;
using Prowl.Editor.ImGUI;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;
using Prowl.Runtime.Rendering.OpenGL;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Silk.NET.Input;

namespace Prowl.Editor;

public static class Program
{
    public static ImGUIController imguiController { get; internal set; }

    public static event Action? OnDrawEditor;
    public static event Action? OnUpdateEditor;

    public static bool IsReloadingExternalAssemblies { get; private set; }
    public static void RegisterReloadOfExternalAssemblies() => IsReloadingExternalAssemblies = true;

    private static bool CreatedDefaultWindows = false;

    public static int Main(string[] args)
    {
        Application.Initialize += () => {
            // Editor-specific initialization code
            imguiController = new(GLDevice.GL, Window.InternalWindow, Input.Context);
            EditorGui.Initialize();
            MonoBehaviour.PauseLogic = true;
            ImporterAttribute.GenerateLookUp();

            // Start with the project window open
            new ProjectsWindow();
        };

        Application.Update += (delta) => {
            imguiController.Update((float)delta);
            EditorGui.SetupDock();

            AssetDatabase.InternalUpdate();

            if (PlayMode.Current == PlayMode.Mode.Editing) // Dont recompile scripts unless were in editor mode
                CheckReloadingAssemblies();

            // Editor-specific update code
            if (Project.HasProject)
            {
                if (!CreatedDefaultWindows)
                {
                    CreatedDefaultWindows = true;
                    new EditorMainMenubar();
                    new HierarchyWindow();
                    new ViewportWindow();
                    new GameWindow();
                    new InspectorWindow();
                    new ConsoleWindow();
                    new AssetBrowserWindow();
                    new AssetsWindow();
                }

                Application.DataPath = Project.ProjectDirectory;

                if (GeneralPreferences.Instance.LockFPS)
                {
                    Window.InternalWindow.VSync = false;
                    Window.InternalWindow.FramesPerSecond = GeneralPreferences.Instance.TargetFPS;
                    Window.InternalWindow.UpdatesPerSecond = GeneralPreferences.Instance.TargetFPS;
                }
                else
                {
                    Window.InternalWindow.FramesPerSecond = 0;
                    Window.InternalWindow.UpdatesPerSecond = 0;
                    Window.InternalWindow.VSync = GeneralPreferences.Instance.VSync;
                }

                if (Hotkeys.IsHotkeyDown("SaveSceneAs", new() { Key = Key.S, Ctrl = true, Shift = true }))
                    MainMenuItems.SaveSceneAs();
                else if (Hotkeys.IsHotkeyDown("SaveScene", new() { Key = Key.S, Ctrl = true }))
                    MainMenuItems.SaveScene();
                else if (Hotkeys.IsHotkeyDown("BuildProject", new() { Key = Key.B, Ctrl = true }))
                    Project.BuildProject();

                Application.isPlaying = PlayMode.Current != PlayMode.Mode.Editing;
                Application.isActivelyPlaying = PlayMode.Current == PlayMode.Mode.Playing;

                if (Application.isActivelyPlaying)
                    Physics.Update();

                try
                {
                    SceneManager.Update();
                }
                catch (Exception e)
                {
                    Debug.LogError("Scene Update Error: " + e.ToString());
                }
            }

            GameWindow.IsFocused = false;
            OnUpdateEditor?.Invoke();
            OnDrawEditor?.Invoke();
        };

        Application.Render += (delta) => {
            Graphics.StartFrame();

            EditorGui.Update();

            Graphics.EndFrame();

            imguiController.Render();
        };

        Application.Quitting += () => {
            imguiController.Dispose();
        };


        Application.Run("Prowl Editor", 1920, 1080, new EditorAssetProvider(), true);

        return 0;
    }

    public static void CheckReloadingAssemblies()
    {
        if (IsReloadingExternalAssemblies)
        {
            IsReloadingExternalAssemblies = false;

            if (Project.HasProject)
            {
                SceneManager.StoreScene();

                try
                {
                    // Unload External Assemblies
                    AssemblyManager.Unload();

                    // Delete everything under Temp\Bin
                    if (Directory.Exists(Path.Combine(Project.TempDirectory, "bin")))
                        Directory.Delete(Path.Combine(Project.TempDirectory, "bin"), true);
                    Directory.CreateDirectory(Path.Combine(Project.TempDirectory, "bin"));

                    // Compile the Projects
                    Project.Compile(Project.Assembly_Proj);
                    Project.Compile(Project.Editor_Assembly_Proj);

                    // Reload the External Assemblies
                    AssemblyManager.LoadExternalAssembly(Project.Editor_Assembly_DLL, true);
                    AssemblyManager.LoadExternalAssembly(Project.Assembly_DLL, true);

                    ImGuiNotify.InsertNotification(new ImGuiToast() {
                        Title = "Project Recompiled!",
                        Content = "Successfully recompiled project scripts.",
                        Color = new Vector4(1f),
                        Type = ImGuiToastType.Success
                    });
                }
                catch (Exception e)
                {
                    Runtime.Debug.LogError($"Error reloading assemblies: {e.Message}");
                    Runtime.Debug.LogError(e.StackTrace);

                    ImGuiNotify.InsertNotification(new ImGuiToast() {
                        Title = "Project Failed to Recompiled!",
                        Content = e.Message,
                        Color = new Vector4(1f),
                        Type = ImGuiToastType.Error
                    });
                }
                finally
                {
                    OnAssemblyLoadAttribute.Invoke();

                    SceneManager.RestoreScene();
                    SceneManager.ClearStoredScene();
                }
            }
            else
            {
                Runtime.Debug.LogError("Cannot reload assemblies, No project loaded.");

                ImGuiNotify.InsertNotification(new ImGuiToast() {
                    Title = "Recompilation Failed!",
                    Content = "No Project Loaded.",
                    Color = new Vector4(0.8f, 0.2f, 0.2f, 1),
                    Type = ImGuiToastType.Error
                });
            }
        }
    }
}
