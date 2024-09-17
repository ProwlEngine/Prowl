// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;

using CommandLine;

using Prowl.Editor.Assets;
using Prowl.Editor.Build;
using Prowl.Editor.Editor.CLI;
using Prowl.Editor.Preferences;
using Prowl.Editor.ProjectSettings;
using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;

namespace Prowl.Editor;

public static class Program
{
    public static void RegisterReloadOfExternalAssemblies() => IsReloadingExternalAssemblies = true;
    private static bool IsReloadingExternalAssemblies { get; set; }
    private static bool s_createdDefaultWindows;
    private static bool s_opened;

    public static int Main(string[] args)
    {
        // CommandLineParser what command line arguments where used. `open` is the default option,
        // so there is no need to call "prowl.exe open", only "prowl.exe".
        return Parser.Default.ParseArguments<CliOpenOptions, CliCreateOptions, CliBuildOptions>(args)
                     .MapResult(
                         // the default option, so there is no need to call "prowl.exe open", only "prowl.exe"
                         (CliOpenOptions options) => Run(options),
                         (CliCreateOptions options) => CreateCommand(options),
                         (CliBuildOptions options) => BuildCommand(options),
                         _ => 1); // the command do not exist, finish the program as an error
    }

    private static int CreateCommand(CliCreateOptions options)
    {
        Console.WriteLine("Creating a new project");

        if (options.ProjectPath is null || options.ProjectPath.Exists)
        {
            Console.WriteLine("Path is not valid or already exists");
            return 1;
        }

        Project.CreateNew(options.ProjectPath);

        return 0;
    }

    private static int Run(CliOpenOptions options)
    {
        // set global Culture to invariant
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        Application.Initialize += () =>
        {
            // Editor-specific initialization code
            EditorGuiManager.Initialize();
            ImporterAttribute.GenerateLookUp();

            // Start with the project window open
            _ = new ProjectsWindow();
        };

        Application.Update += () =>
        {
            AssetDatabase.InternalUpdate();

            if (PlayMode.Current == PlayMode.Mode.Editing) // Don't recompile scripts unless were in editor mode
                CheckReloadingAssemblies();

            if (!Project.HasProject)
            {
                return;
            }

            Physics.Initialize();

            // Editor-specific update code
            if (!s_createdDefaultWindows)
            {
                s_createdDefaultWindows = true;

                var console = EditorGuiManager.DockWindowTo(new ConsoleWindow(), null, Docking.DockZone.Center);
                var assetBrowser =
                    EditorGuiManager.DockWindowTo(new AssetsBrowserWindow(), console, Docking.DockZone.Center);
                // Add Asset Tree, When we do this AssetBrowser node will subdivide into two children
                var assetTree = EditorGuiManager.DockWindowTo(new AssetsTreeWindow(), assetBrowser,
                    Docking.DockZone.Left, 0.2f);
                // So for the Inspector we need to use the Child to dock now
                EditorGuiManager.DockWindowTo(new InspectorWindow(), assetBrowser?.Child[1],
                    Docking.DockZone.Right, 0.75f);
                // Now Asset Browser is Subdivided twice,
                assetBrowser = assetBrowser?.Child[1].Child[0];
                var game = EditorGuiManager.DockWindowTo(new GameWindow(), assetBrowser, Docking.DockZone.Top,
                    0.65f);
                EditorGuiManager.DockWindowTo(new SceneViewWindow(), game, Docking.DockZone.Center);

                // and finally hierarchy on top of asset tree
                EditorGuiManager.DockWindowTo(new HierarchyWindow(), assetTree,
                    Docking.DockZone.Top, 0.65f);
            }

            Application.DataPath = Project.Active?.ProjectPath;

            if (GeneralPreferences.Instance.LockFPS)
            {
                Graphics.VSync = false;
                Screen.FramesPerSecond = GeneralPreferences.Instance.TargetFPS;
            }
            else
            {
                Graphics.VSync = GeneralPreferences.Instance.VSync;
                Screen.FramesPerSecond = 0;
            }

            if (Hotkeys.IsHotkeyDown("SaveSceneAs", new() { Key = Key.S, Ctrl = true, Shift = true }))
                EditorGuiManager.SaveSceneAs();
            else if (Hotkeys.IsHotkeyDown("SaveScene", new() { Key = Key.S, Ctrl = true }))
                EditorGuiManager.SaveScene();

            Application.isPlaying = PlayMode.Current == PlayMode.Mode.Playing;

            try
            {
                bool hasGameWindow = GameWindow.LastFocused.IsAlive;
                // Push GameWindow's input handler
                if (hasGameWindow) Input.PushHandler((GameWindow.LastFocused.Target as GameWindow)!.InputHandler);

                PlayMode.GameTime.Update();
                Time.TimeStack.Push(PlayMode.GameTime);
                SceneManager.Update();
                Time.TimeStack.Pop();

                if (hasGameWindow) Input.PopHandler();
            }
            catch (Exception e)
            {
                Debug.LogError($"Scene Update Error: {e}");
            }
        };

        Application.Render += () =>
        {
            EditorGuiManager.Update();

            if (!s_opened && options.ProjectPath is not null && options.ProjectPath.Exists)
            {
                Project.Open(new Project(options.ProjectPath));
                s_opened = true;
            }

            Graphics.EndFrame();
        };

        Application.Quitting += () => { };

        Application.Run("Prowl Editor", 1920, 1080, new EditorAssetProvider(), true);

        return 0;
    }

    private static int BuildCommand(CliBuildOptions options)
    {
        Console.WriteLine($"Building project from\t'{options.ProjectPath}'");
        if (options.ProjectPath is null || !options.ProjectPath.Exists)
        {
            Console.WriteLine("Path is not valid or already exists");
            return 1;
        }

        var pathBuild = new DirectoryInfo(Path.Combine(options.ProjectPath.ToString(), "Builds",
            DateTime.UtcNow.ToString("yyyyMMddHHmmss")));

        Console.WriteLine($"Building path\t\t'{pathBuild}'");
        if (pathBuild.Exists)
        {
            Console.WriteLine("Build path is not valid or already exists");
            return 1;
        }

        var project = new Project(options.ProjectPath);
        Project.Open(project);
        Application.DataPath = options.ProjectPath.ToString();
        pathBuild.Create();
        var builders = ProjectBuilder.GetAll().ToList();
        Application.AssetProvider = new EditorAssetProvider();
        builders[0]?.StartBuild(BuildProjectSetting.Instance.Scenes, pathBuild);
        return 0;
    }

    private static void CheckReloadingAssemblies()
    {
        if (!IsReloadingExternalAssemblies)
        {
            return;
        }

        IsReloadingExternalAssemblies = false;

        if (Project.Active is not null && Project.HasProject)
        {
            SceneManager.StoreScene();
            SceneManager.Clear();

            try
            {
                // Unload External Assemblies
                AssemblyManager.Unload();

                var active = Project.Active;

                DirectoryInfo temp = active.TempDirectory;
                DirectoryInfo bin = new DirectoryInfo(Path.Combine(temp.FullName, "bin"));
                DirectoryInfo debug = new DirectoryInfo(Path.Combine(bin.FullName, "Debug"));

                // Delete everything under Temp\Bin
                if (bin.Exists)
                    Directory.Delete(bin.FullName, true);
                bin.Create();

                // Compile the Projects
                Project.Compile(active.Assembly_Proj.FullName, debug);
                Project.Compile(active.Editor_Assembly_Proj.FullName, debug);

                // Reload the External Assemblies
                AssemblyManager.LoadExternalAssembly(active.Editor_Assembly_DLL.FullName, true);
                AssemblyManager.LoadExternalAssembly(active.Assembly_DLL.FullName, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error reloading assemblies: {e.Message}");
                Debug.LogError($"{e.StackTrace}");
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
            Debug.LogError("Cannot reload assemblies, No project loaded.");
        }
    }
}
