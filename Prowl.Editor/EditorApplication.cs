using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Prowl.Editor.Assets;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.PropertyDrawers;
using ImGuiNET;
using Raylib_cs;
using System.Diagnostics;
using System.Numerics;

namespace Prowl.Editor;

public unsafe class EditorApplication : Application {

    public static new EditorApplication Instance { get; private set; }

    public static Action OnDrawEditor;
    public static Action OnUpdateEditor;

    public bool IsReloadingExternalAssemblies { get; private set; }
    public void RegisterReloadOfExternalAssemblies() => IsReloadingExternalAssemblies = true;

    public override void Initialize()
    {
        // CompileExternalAssemblies();
        base.Initialize();
        Instance = this;

        EditorGui.Initialize();
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

        while (IsRunning)
        {
            CheckReloadingAssemblies();

            float updateTime = (float)updateTimer.Elapsed.TotalSeconds;
            Time.Update(updateTime);
            updateTimer.Restart();

            if (Project.HasProject)
            {
                SceneManager.Update();

                float physicsTime = (float)physicsTimer.Elapsed.TotalSeconds;
                if (physicsTime > Time.fixedDeltaTime)
                {
                    SceneManager.PhysicsUpdate();
                    physicsTimer.Restart();
                }
            }

            controller.Update(updateTime);
            ImGui.DockSpaceOverViewport(ImGui.GetMainViewport());

            OnUpdateEditor?.Invoke();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Raylib_cs.Color.DARKGRAY);

            OnDrawEditor?.Invoke();
            EditorGui.Update(); 
            if (Project.HasProject)
                SceneManager.Draw();
            controller.Draw();

            Raylib.EndDrawing();

            if (Raylib.WindowShouldClose())
                Terminate();
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
                var gos = SceneManager.AllGameObjects.Where(x => !x.hideFlags.HasFlag(HideFlags.DontSave) && !x.hideFlags.HasFlag(HideFlags.HideAndDontSave)).ToArray();
                var s = JsonUtility.Serialize(gos);

                foreach (var go in SceneManager.AllGameObjects)
                    go.Destroy();
                EngineObject.HandleDestroyed();

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
                    //Hierarchy.SaveToAssetPath();
                    SceneManager.Clear();

                    ClearTypeDescriptorCache();
                    //PhysicsEngine.World = null;
                    return true;
                });

                // Update Property Drawers - Editor project can add them so this goes after
                PropertyDrawer.GenerateLookUp();
                CustomEditorAttribute.GenerateLookUp();
                MenuItem.FindAllMenus();
                //AssetProvider.GenerateLookUp();
                ImporterAttribute.GenerateLookUp();
                _AssemblyManager.AddUnloadTask(() =>
                {
                    Selection.Clear();
                    PropertyDrawer.ClearLookUp();
                    ImporterAttribute.ClearLookUp();
                    CustomEditorAttribute.ClearLookUp();
                    MenuItem.ClearMenus();
                    //AssetProvider.ClearLookUp();
                    return true;
                });

                // Just deserializing should be enough
                JsonUtility.Deserialize<GameObject[]?>(s);

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
