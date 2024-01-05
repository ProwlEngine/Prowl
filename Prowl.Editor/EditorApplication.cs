using HexaEngine.ImGuiNET;
using Prowl.Editor.Assets;
using Prowl.Editor.Drawers.NodeSystem;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.PropertyDrawers;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Input;
using System.Text.Json;
using static Prowl.Editor.EditorConfiguration;

namespace Prowl.Editor;

public class EditorConfiguration
{
    public class Hotkey
    {
        public Key Key { get; set; }

        public bool Ctrl { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
    }

    public Dictionary<string, Hotkey> hotkeys { get; set; } = new();

}

public unsafe class EditorApplication : Application {

    public static new EditorApplication Instance { get; private set; }

    public static event Action? OnDrawEditor;
    public static event Action? OnUpdateEditor;

    static bool hasDockSetup = false;

    public static EditorConfiguration EditorConfig = new();

    public bool IsReloadingExternalAssemblies { get; private set; }
    public void RegisterReloadOfExternalAssemblies() => IsReloadingExternalAssemblies = true;

    public static void SaveConfig()
    {
        string filePath = Path.Combine(Project.Projects_Directory, "EditorConfig.setting");
        string json = JsonSerializer.Serialize(EditorConfig);

        // Ensure Directory Exists - ReCore67
        Directory.CreateDirectory(Project.Projects_Directory);

        File.WriteAllText(filePath, json);
    }

    public static bool IsHotkeyDown(string name, Hotkey defaultKey)
    {
        if (EditorConfig.hotkeys.TryGetValue(name, out var hotkey))
        {
            if (Input.GetKeyDown(hotkey.Key)) {
                bool ctrl = Input.GetKey(Key.ControlLeft) == hotkey.Ctrl;
                bool alt = Input.GetKey(Key.AltLeft) == hotkey.Alt;
                bool shift = Input.GetKey(Key.ShiftLeft) == hotkey.Shift;
                if (ctrl && alt && shift)
                    return true;
            }
            return Input.GetKeyDown(hotkey.Key) && Input.GetKey(Key.ControlLeft) == hotkey.Ctrl && Input.GetKey(Key.AltLeft) == hotkey.Alt && Input.GetKey(Key.ShiftLeft) == hotkey.Shift;
        }
        else
        {
            EditorConfig.hotkeys.Add(name, defaultKey);
            SaveConfig();
        }
        return false;
    }

    public override void Initialize()
    {
        Window.InitWindow("Prowl", 1920, 1080, Silk.NET.Windowing.WindowState.Normal, true);

        Window.Load += () => {

            EditorGui.Initialize();

            SceneManager.Initialize();
            Physics.Initialize();

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            // Immediately start with pausing all components, since were in editor we dont want them running just yet
            MonoBehaviour.PauseLogic = true;

            // Early Importer Initialization
            ImporterAttribute.GenerateLookUp();

            // Start with the project window open
            new ProjectsWindow();

            Runtime.Debug.LogSuccess("Initialization complete");
        };

        Window.Update += (delta) => {
            try {
                AssetDatabase.Update();

                CheckReloadingAssemblies();

                Time.Update(delta);

                if (Project.HasProject) {

                    //var setting = Project.ProjectSettings.GetSetting<ApplicationSettings>();
                    Project.ProjectSettings.GetSetting<BuildSettings>(); // Called to ensure the Editor Ui exists
                    EditorSettings Settings = Project.ProjectSettings.GetSetting<EditorSettings>();

                    Window.InternalWindow.VSync = Settings.VSync;
                    //Window.InternalWindow.FramesPerSecond = 60;
                    //Window.InternalWindow.UpdatesPerSecond = 60;

                    if (IsHotkeyDown("SaveSceneAs", new Hotkey() { Key = Key.S, Ctrl = true, Shift = true }))
                        MainMenuItems.SaveSceneAs();
                    else if (IsHotkeyDown("SaveScene", new Hotkey() { Key = Key.S, Ctrl = true }))
                        MainMenuItems.SaveScene();
                    else if (IsHotkeyDown("BuildProject", new Hotkey() { Key = Key.B, Ctrl = true }))
                        Project.BuildProject();

                    isPlaying = PlayMode.Current != PlayMode.Mode.Editing;
                    isActivelyPlaying = PlayMode.Current == PlayMode.Mode.Playing;

                    SceneManager.Update();
                    if (isActivelyPlaying)
                        Physics.Update();
                }

                int dockspaceID = ImGui.DockSpaceOverViewport();

                if (hasDockSetup == false) {
                    ImGui.DockBuilderRemoveNode(dockspaceID);
                    ImGui.DockBuilderAddNode(dockspaceID, ImGuiDockNodeFlags.None);
                    ImGui.DockBuilderSetNodeSize(dockspaceID, ImGui.GetMainViewport().Size);

                    int dock_id_main_right = 0;
                    int dock_id_main_left = 0;
                    ImGui.DockBuilderSplitNode(dockspaceID, ImGuiDir.Right, 0.2f, ref dock_id_main_right, ref dock_id_main_left);
                    int dock_id_main_right_top = 0;
                    int dock_id_main_right_bottom = 0;
                    ImGui.DockBuilderSplitNode(dock_id_main_right, ImGuiDir.Up, 0.35f, ref dock_id_main_right_top, ref dock_id_main_right_bottom);

                    ImGui.DockBuilderDockWindow(FontAwesome6.FolderTree + " Hierarchy", dock_id_main_right_top);
                    ImGui.DockBuilderDockWindow(FontAwesome6.BookOpen + " Inspector", dock_id_main_right_bottom);

                    int dock_id_main_left_top = 0;
                    int dock_id_main_left_bottom = 0;
                    ImGui.DockBuilderSplitNode(dock_id_main_left, ImGuiDir.Down, 0.3f, ref dock_id_main_left_bottom, ref dock_id_main_left_top);
                    ImGui.DockBuilderDockWindow(FontAwesome6.Gamepad + " Game", dock_id_main_left_top);
                    ImGui.DockBuilderDockWindow(FontAwesome6.Camera + " Viewport", dock_id_main_left_top);

                    int dock_id_main_left_bottom_left = 0;
                    int dock_id_main_left_bottom_right = 0;
                    ImGui.DockBuilderSplitNode(dock_id_main_left_bottom, ImGuiDir.Left, 0.25f, ref dock_id_main_left_bottom_left, ref dock_id_main_left_bottom_right);
                    ImGui.DockBuilderDockWindow(FontAwesome6.BoxOpen + " Asset Browser", dock_id_main_left_bottom_right);
                    ImGui.DockBuilderDockWindow(FontAwesome6.Terminal + " Console", dock_id_main_left_bottom_right);
                    ImGui.DockBuilderDockWindow(FontAwesome6.FolderTree + " Assets", dock_id_main_left_bottom_left);

                    ImGui.DockBuilderFinish(dockspaceID);
                    hasDockSetup = true;
                }

                OnUpdateEditor?.Invoke();
                OnDrawEditor?.Invoke();
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        };

        Window.Render += (delta) => {
            Graphics.StartFrame();

            EditorGui.Update();

            Graphics.EndFrame();
        };

        Window.Closing += () => {
            isRunning = false;
            Physics.Dispose();
            Runtime.Debug.Log("Is terminating...");
        };

        Instance = this;

        // Load Editor Config
        string filePath = Path.Combine(Project.Projects_Directory, "EditorConfig.setting");
        if (File.Exists(filePath))
        {
            string json = File.ReadAllText(filePath);
            EditorConfig = JsonSerializer.Deserialize<EditorConfiguration>(json) ?? new();
        }
        else
        {
            SaveConfig();
        }

        isEditor = true;
        isRunning = true;
    }

    public void CheckReloadingAssemblies()
    {
        if (IsReloadingExternalAssemblies)
        {
            IsReloadingExternalAssemblies = false;

            if (Project.HasProject)
            {
                // Serialize the Scene manually to save its state
                //var gos = GameObjectManager.AllGameObjects.Where(x => !x.hideFlags.HasFlag(HideFlags.DontSave) && !x.hideFlags.HasFlag(HideFlags.HideAndDontSave)).ToArray();
                //var s = JsonUtility.Serialize(gos);

                // Unload External Assemblies
                _AssemblyManager.Unload();

                // Delete everything under Temp\Bin
                if (Directory.Exists(Path.Combine(Project.TempDirectory, "bin")))
                    Directory.Delete(Path.Combine(Project.TempDirectory, "bin"), true);
                Directory.CreateDirectory(Path.Combine(Project.TempDirectory, "bin"));

                // Compile the Projects
                Project.Compile(Project.Assembly_Proj);
                Project.Compile(Project.Editor_Assembly_Proj);

                // Reload the External Assemblies
                _AssemblyManager.LoadExternalAssembly(Project.Editor_Assembly_DLL, true);
                _AssemblyManager.LoadExternalAssembly(Project.Assembly_DLL, true);

                _AssemblyManager.AddUnloadTask(() =>
                {
                    foreach (var go in SceneManager.AllGameObjects)
                        go.Destroy();
                    EngineObject.HandleDestroyed();

                    SceneManager.Clear();

                    PropertyDrawer.ClearLookUp();
                    ImporterAttribute.ClearLookUp();
                    CustomEditorAttribute.ClearLookUp();
                    NodeSystemDrawer.ClearLookUp();
                    MenuItem.ClearMenus();

                    ClearTypeDescriptorCache();
                    //PhysicsEngine.World = null;
                    return true;
                });

                // Update Property Drawers - Editor project can add them so this goes after
                PropertyDrawer.GenerateLookUp();
                ImporterAttribute.GenerateLookUp();
                CustomEditorAttribute.GenerateLookUp();
                NodeSystemDrawer.GenerateLookUp();
                MenuItem.FindAllMenus();
                CreateAssetMenuHandler.FindAllMenus(); // Injects into Menuitem so doesnt need to Unload

                // Just deserializing should be enough
                //JsonUtility.Deserialize<GameObject[]?>(s);

                ImGuiNotify.InsertNotification(new ImGuiToast()
                {
                    Title = "Project Recompiled!",
                    Content = "Successfully recompiled project scripts.",
                    Color = new Vector4(1f),
                    Type = ImGuiToastType.Success
                });
            }
            else
            {
                Runtime.Debug.LogError("Cannot reload assemblies, No project loaded.");

                ImGuiNotify.InsertNotification(new ImGuiToast()
                {
                    Title = "Recompilation Failed!",
                    Content = "No Project Loaded.",
                    Color = new Vector4(0.8f, 0.2f, 0.2f, 1),
                    Type = ImGuiToastType.Error
                });
            }
        }
    }
}
