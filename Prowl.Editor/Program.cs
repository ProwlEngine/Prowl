using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Theming;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;


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


    private static void LogCommands(List<string> args)
    {
        foreach ((string[] aliases, Action<List<string>> a, int m, string desc) in s_commands)
        {
            if (args.Count == 0 || Array.Exists(aliases, (x) => x.Equals(args[0])))
                Console.WriteLine($"[{string.Join(", ", aliases)}]: {desc}");
        }

        Environment.Exit(0);
    }


    private static (string[] commandAliases, Action<List<string>> commandAction, int mandatoryArgs, string description)[] s_commands =
    [
        (["--help", "-help", "-h"], LogCommands, -1,
            "Displays help information for a specific command, or all commands if no command is specified"),

        (["--project", "-project", "-p"], (args) => StartupProjectPath = args[0], 1,
            "Opens the provided source project path directly (skips launcher)"),

        (["--restore-scene", "-restore", "-r"], (args) => RestoreScenePath = args[0], 1,
            "Loads the provided scene as the default scene to open instead of the last saved scene"),

        (["--buildmode", "-build", "-b"], (args) => { BuildMode = true; StartupProjectPath = args[0]; }, 1,
            "Builds a project directly without opening the editor UI"),

        (["--output", "-output", "-o"], (args) => BuildOutputPath = args[0], 1,
            "Specifies the build output path when the editor builds a project"),
    ];


    private static void ReadArguments(string[] args)
    {
        Queue<string> argQueue = new(args);

        while (argQueue.TryDequeue(out string result))
        {
            (string[]? a, Action<List<string>>? action, int argsCount, string? d) =
                Array.Find(s_commands, (x) => Array.Exists(x.commandAliases, (x) => x.Equals(result)));

            if (action == null)
                throw new Exception($"Unknown argument '{result}'");

            List<string> cmdArgs = [];

            while (argQueue.TryPeek(out string next))
            {
                // Next argument is a command
                if (next.StartsWith('-'))
                    break;

                if (argsCount > -1 && cmdArgs.Count + 1 > argsCount)
                    throw new Exception($"Too many arguments specified for '{result}'");

                cmdArgs.Add(argQueue.Dequeue());
            }

            if (cmdArgs.Count < argsCount)
                throw new Exception($"Not enough arguments specified for '{result}'");

            action.Invoke(cmdArgs);
        }
    }


    public static void Main(string[] args)
    {
        RegisterMiniAudioExResolver();

        ReadArguments(args);

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

            Build.ProjectBuilder.StartBuildAsync(false, BuildOutputPath ?? StartupProjectPath + "/../Builds");
            return;
        }

        var editor = new EditorApplication();
        editor.Run("Prowl Editor", 1920, 1080);

        //Runtime.Window.InternalWindow.WindowState = EditorSettings.Instance.WindowMaximized ? Silk.NET.Windowing.WindowState.Maximized : Silk.NET.Windowing.WindowState.Normal;
    }

    /// <summary>
    /// "miniaudioex" is the only native audio library that isn't a real NuGet native-asset package
    /// (it's manually vendored via Prowl.Runtime.csproj's CopyLibraries target into runtimes/&lt;rid&gt;/native/),
    /// so a self-contained Editor publish never flattens it to the app root and the default probing
    /// doesn't find it there either - it must be resolved explicitly.
    /// This is registered here (Editor-only), not inside Prowl.Runtime.dll: exported Player builds
    /// already register their own DllImportResolver for that same assembly (DesktopBuildPipeline's
    /// generated Program.cs), and NativeLibrary.SetDllImportResolver allows only one resolver per
    /// assembly - a second registration throws.
    /// </summary>
    private static void RegisterMiniAudioExResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(Prowl.Runtime.Game).Assembly, (libraryName, _, _) =>
        {
            if (libraryName != "miniaudioex")
                return IntPtr.Zero;

            string? os = OperatingSystem.IsWindows() ? "win"
                       : OperatingSystem.IsMacOS() ? "osx"
                       : OperatingSystem.IsLinux() ? "linux"
                       : null;

            // ProcessArchitecture (not OSArchitecture): a 32-bit process on a 64-bit Windows OS must
            // load the x86 native lib, not x64 - Libraries/win-x86 exists precisely for that case.
            string? arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => null,
            };

            if (os == null || arch == null)
                return IntPtr.Zero;

            string fileName = OperatingSystem.IsWindows() ? "miniaudioex.dll"
                             : OperatingSystem.IsMacOS() ? "libminiaudioex.dylib"
                             : "libminiaudioex.so";

            string path = Path.Combine(AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", fileName);
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        });
    }
}
