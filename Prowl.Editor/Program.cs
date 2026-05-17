using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Theming;

namespace Prowl.Editor;

public static class Program
{
    /// <summary>If set via --project arg, the editor opens this project directly (skips launcher).</summary>
    public static string? StartupProjectPath { get; private set; }

    /// <summary>If set via --restore-scene arg, this scene is loaded instead of the last saved scene.</summary>
    public static string? RestoreScenePath { get; private set; }

    /// <summary>If set via --buildmode arg, the editor runs a build directly and doesn't start any UI.</summary>
    public static bool BuildMode { get; private set; } = false;

    /// <summary>If set via --output arg, it builds the game into this path when built through the --build arg.</summary>
    public static string? BuildOutputPath { get; private set; }

    public static void Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length)
                StartupProjectPath = args[++i];
            else if (args[i] == "--restore-scene" && i + 1 < args.Length)
                RestoreScenePath = args[++i];
            else if ((args[i] == "--buildmode" || args[i] == "-build" || args[i] == "-b") && i + 1 < args.Length && !args[i+1].StartsWith('-'))
            {
                BuildMode = true;
                StartupProjectPath = args[++i];
            }
            else if ((args[i] == "--output" || args[i] == "-output" || args[i] == "-o") && i + 1 < args.Length)
            {
                BuildOutputPath = args[++i];
            }
        }

        if (BuildMode)
        {
            ProjectSettingsRegistry.Initialize();

            var project = Project.Open(StartupProjectPath);
            project.SetActive();

            // Load user script assemblies before registry scanning
            ScriptAssemblyManager.LoadAssemblies(project);


            // Initialize asset database for the already-opened project
            var db = new EditorAssetDatabase(Project.Current!);
            db.Initialize();

            // Load project settings
            ProjectSettingsRegistry.OnProjectOpened();


            Build.ProjectBuilder.StartBuildAsync(false, BuildOutputPath ?? StartupProjectPath+"/../Builds");
            return;
        }



        var instance = EditorSettings.Instance;

        var editor = new EditorApplication();
        //editor.Run("Prowl Editor", instance.WindowWidth, instance.WindowHeight);

        editor.Run("Prowl Editor", 1200, 800);

        Runtime.Window.InternalWindow.WindowState = EditorSettings.Instance.WindowMaximized ? Silk.NET.Windowing.WindowState.Maximized : Silk.NET.Windowing.WindowState.Normal;
    }
}
