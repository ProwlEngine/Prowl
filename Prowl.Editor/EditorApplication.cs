using HexaEngine.ImGuiNET;
using Prowl.Editor.Assets;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Raylib_cs;
using System.Diagnostics;
using System.Text.Json;
using static Prowl.Editor.EditorConfiguration;

namespace Prowl.Editor;

public class EditorConfiguration
{
    public class Hotkey
    {
        public KeyboardKey Key { get; set; }

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
        File.WriteAllText(filePath, json);
    }

    public static bool IsHotkeyDown(string name, Hotkey defaultKey)
    {
        if (EditorConfig.hotkeys.TryGetValue(name, out var hotkey))
        {
            return Raylib.IsKeyDown(hotkey.Key) && Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_CONTROL) == hotkey.Ctrl && Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_ALT) == hotkey.Alt && Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) == hotkey.Shift;
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
        // CompileExternalAssemblies();
        base.Initialize();
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

        EditorGui.Initialize();

        isEditor = true;
    }

    protected override void Loop()
    {
        Stopwatch updateTimer = new();
        Stopwatch physicsTimer = new();
        updateTimer.Start();
        physicsTimer.Start();

        // Immediately start with pausing all components, since were in editor we dont want them running just yet
        MonoBehaviour.PauseLogic = true;

        // Early Importer Initialization
        ImporterAttribute.GenerateLookUp();

        // Start with the project window open
        new ProjectsWindow();

        while (isRunning)
        {
            CheckReloadingAssemblies();

            float updateTime = (float)updateTimer.Elapsed.TotalSeconds;
            Time.Update(updateTime);
            updateTimer.Restart();

            if (Project.HasProject)
            {
                //var setting = Project.ProjectSettings.GetSetting<ApplicationSettings>();
                Project.ProjectSettings.GetSetting<BuildSettings>(); // Called to ensure the Editor Ui exists

                if (IsHotkeyDown("SaveSceneAs", new Hotkey() { Key = KeyboardKey.KEY_S, Ctrl = true, Shift = true }))
                    MainMenuItems.SaveSceneAs();
                else if (IsHotkeyDown("SaveScene", new Hotkey() { Key = KeyboardKey.KEY_S, Ctrl = true }))
                    MainMenuItems.SaveScene();
                else if (IsHotkeyDown("BuildProject", new Hotkey() { Key = KeyboardKey.KEY_B, Ctrl = true }))
                    Project.BuildProject();

                isPlaying = PlayMode.Current != PlayMode.Mode.Editing;

                SceneManager.Update();

                float physicsTime = (float)physicsTimer.Elapsed.TotalSeconds;
                if (physicsTime > Time.fixedDeltaTime)
                {
                    SceneManager.PhysicsUpdate();
                    physicsTimer.Restart();
                }
            }

            controller.Update(updateTime);
            int dockspaceID = ImGui.DockSpaceOverViewport(ImGui.GetMainViewport());

            if (hasDockSetup == false)
            {
                ImGui.DockBuilderRemoveNode(dockspaceID);
                ImGui.DockBuilderAddNode(dockspaceID, ImGuiDockNodeFlags.None);
                ImGui.DockBuilderSetNodeSize(dockspaceID, ImGui.GetMainViewport().Size);

                int dock_id_main_right = 0;
                int dock_id_main_left = 0;
                ImGui.DockBuilderSplitNode(dockspaceID, ImGuiDir.Right, 0.2f, ref dock_id_main_right, ref dock_id_main_left);
                int dock_id_main_right_top = 0;
                int dock_id_main_right_bottom = 0;
                ImGui.DockBuilderSplitNode(dock_id_main_right, ImGuiDir.Up, 0.35f, ref dock_id_main_right_top, ref dock_id_main_right_bottom);

                ImGui.DockBuilderDockWindow("Hierarchy", dock_id_main_right_top);
                ImGui.DockBuilderDockWindow("Inspector", dock_id_main_right_bottom);

                int dock_id_main_left_top = 0;
                int dock_id_main_left_bottom = 0;
                ImGui.DockBuilderSplitNode(dock_id_main_left, ImGuiDir.Down, 0.3f, ref dock_id_main_left_bottom, ref dock_id_main_left_top);
                ImGui.DockBuilderDockWindow("Game", dock_id_main_left_top);
                ImGui.DockBuilderDockWindow("Viewport", dock_id_main_left_top);

                int dock_id_main_left_bottom_left = 0;
                int dock_id_main_left_bottom_right = 0;
                ImGui.DockBuilderSplitNode(dock_id_main_left_bottom, ImGuiDir.Left, 0.25f, ref dock_id_main_left_bottom_left, ref dock_id_main_left_bottom_right);
                ImGui.DockBuilderDockWindow("Asset Browser", dock_id_main_left_bottom_right);
                ImGui.DockBuilderDockWindow("Console", dock_id_main_left_bottom_right);
                ImGui.DockBuilderDockWindow("Assets", dock_id_main_left_bottom_left);

                ImGui.DockBuilderFinish(dockspaceID);
                hasDockSetup = true;
            }

            OnUpdateEditor?.Invoke();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Raylib_cs.Color.DARKGRAY);

            OnDrawEditor?.Invoke();
            EditorGui.Update(); 
            // Editor doesnt draw the normal way, Rendering is done entirely manually
            //if (Project.HasProject)
            //    GameObjectManager.Draw();
            controller.Draw();

            Raylib.EndDrawing();

            if (Raylib.WindowShouldClose())
                Quit();
        }

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
                    Selection.Clear();

                    PropertyDrawer.ClearLookUp();
                    ImporterAttribute.ClearLookUp();
                    CustomEditorAttribute.ClearLookUp();
                    MenuItem.ClearMenus();

                    ClearTypeDescriptorCache();
                    //PhysicsEngine.World = null;
                    return true;
                });

                // Update Property Drawers - Editor project can add them so this goes after
                PropertyDrawer.GenerateLookUp();
                ImporterAttribute.GenerateLookUp();
                CustomEditorAttribute.GenerateLookUp();
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
