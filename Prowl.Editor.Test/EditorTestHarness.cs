// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.Build;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Base class for headless editor tests. Each test gets a fresh throwaway project on disk with a live
/// <see cref="EditorAssetBackend"/>, and everything is torn down (including the temp directory) when
/// the test finishes.
///
/// This deliberately avoids the editor GUI/graphics layer: never instantiate EditorApplication and
/// never import graphics-bound assets (textures/shaders/models), since <c>Graphics.GL</c> is unguarded.
/// Tests in this assembly run without parallelization (see TestAssemblyConfig) because the editor uses
/// global static state.
/// </summary>
public abstract class EditorTestHarness : IDisposable
{
    private readonly string _root;
    private readonly bool _prevIsEditor;
    private readonly bool _prevIsPlaying;

    /// <summary>The throwaway project for this test.</summary>
    protected Project Project { get; }

    /// <summary>The live asset database for the project.</summary>
    protected EditorAssetBackend Assets { get; private set; }

    protected EditorTestHarness()
    {
        _prevIsEditor = Application.IsEditor;
        _prevIsPlaying = Application.IsPlaying;
        Application.IsEditor = true;
        Application.IsPlaying = false;

        _root = Path.Combine(Path.GetTempPath(), "ProwlEditorTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        Project = Project.Create(_root, "TestProject");
        Project.SetActive(addToRecent: false); // don't pollute the user's recent-projects list

        Assets = new EditorAssetBackend(Project);
        Assets.Initialize(); // registers itself as AssetDatabase.Current
    }

    /// <summary>Absolute path to a path relative to the project's Assets folder.</summary>
    protected string AssetAbsolutePath(string relativePath) => Path.Combine(Project.AssetsPath, relativePath);

    /// <summary>
    /// Writes a GameObject hierarchy to disk as a .prefab asset and imports it. Returns the asset GUID.
    /// The file is serialized as a GameObject; the PrefabImporter wraps it into a PrefabAsset.
    /// </summary>
    protected Guid CreatePrefabAsset(GameObject source, string relativePath = "Prefab.prefab")
    {
        source.ClearPrefabDataRecursive();

        Guid savedId = source.AssetID;
        source.AssetID = Guid.Empty;
        EchoObject echo = Serializer.Serialize(typeof(object), source);
        source.AssetID = savedId;

        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, echo.WriteToString());

        return Assets.ImportFile(relativePath);
    }

    /// <summary>Resolve a prefab asset by GUID.</summary>
    protected PrefabAsset? GetPrefab(Guid guid) => AssetDatabase.Get(guid) as PrefabAsset;

    /// <summary>Persist a Scene as a .scene asset and return its GUID.</summary>
    protected Guid CreateSceneAsset(Scene scene, string relativePath = "Scene.scene")
    {
        Assets.CreateAsset(scene, relativePath);
        return scene.AssetID;
    }

    /// <summary>
    /// Disposes the current asset database and opens a fresh one over the same project, simulating
    /// closing and re-opening the editor (re-scan, reading .meta files and the metadata cache).
    /// </summary>
    protected EditorAssetBackend ReopenDatabase()
    {
        Assets.Dispose();
        Assets = new EditorAssetBackend(Project);
        Assets.Initialize();
        return Assets;
    }

    // ================================================================
    //  Shared helpers for script/plugin/asmdef/build tests
    // ================================================================

    /// <summary>Write a file (usually a .cs script) relative to the project's Assets folder.</summary>
    protected void WriteScript(string relativePath, string content)
    {
        string abs = Path.Combine(Project.AssetsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    /// <summary>
    /// Write a script, compile, assert success, and load the fresh game assembly from bytes. Each call yields a
    /// distinct <see cref="Assembly"/>, so overwriting the same file and calling again gives the "v2".
    /// </summary>
    protected Assembly CompileGameAssembly(string relativePath, string code)
    {
        WriteScript(relativePath, code);
        return CompileGameAssembly();
    }

    private readonly Dictionary<Assembly, byte[]> _compiledBytes = new();

    protected Func<Assembly, byte[]?> AssemblyBytesResolver => asm => _compiledBytes.TryGetValue(asm, out var bytes) ? bytes : null;

    /// <summary>Compile whatever scripts are on disk, assert success, and load the fresh game assembly.</summary>
    protected Assembly CompileGameAssembly()
    {
        var result = ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, $"Compilation failed: {result.Errors}");
        byte[] bytes = File.ReadAllBytes(Project.GameAssemblyPath);
        var asm = Assembly.Load(bytes);
        _compiledBytes[asm] = bytes;
        return asm;
    }

    /// <summary>Enters play mode with a deterministic time source until the returned scope is disposed.</summary>
    protected static PlayModeScope EnterPlayMode() => new();

    /// <summary>Advances full frames: FixedUpdate then Update.</summary>
    protected static void Tick(Scene scene, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            scene.FixedUpdate();
            scene.Update();
        }
    }

    /// <summary>Runs Scene.Update the given number of frames (no physics step).</summary>
    protected static void UpdateScene(Scene scene, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
            scene.Update();
    }

    protected sealed class PlayModeScope : IDisposable
    {
        private readonly bool _wasPlaying;
        private readonly bool _wasEditor;
        private readonly TimeData _time;

        internal PlayModeScope()
        {
            _wasPlaying = Application.IsPlaying;
            _wasEditor = Application.IsEditor;
            Application.IsPlaying = true;
            Application.IsEditor = false;

            _time = new TimeData { DeltaTime = Time.FixedDeltaTime };
            Time.TimeStack.Push(_time);
        }

        public void Dispose()
        {
            if (Time.TimeStack.Count > 0 && Time.TimeStack.Peek() == _time)
                Time.TimeStack.Pop();
            Application.IsPlaying = _wasPlaying;
            Application.IsEditor = _wasEditor;
        }
    }

    /// <summary>Write an assembly definition (.asmdef) into a folder under Assets.</summary>
    protected void WriteAssemblyDefinition(string relativeFolder, AssemblyDefinition def)
    {
        string dir = Path.Combine(Project.AssetsPath, relativeFolder);
        Directory.CreateDirectory(dir);
        def.WriteToFile(Path.Combine(dir, def.Name + AssemblyDefinitionDatabase.Extension));
    }

    /// <summary>Copy a file into the project's Assets folder (creating parent folders).</summary>
    protected void CopyIntoAssets(string sourceFile, string relativeDest)
    {
        string abs = Path.Combine(Project.AssetsPath, relativeDest);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.Copy(sourceFile, abs, true);
    }

    /// <summary>Author a scene containing a single empty GameObject and save it as Main.scene.</summary>
    protected Guid AuthorEmptyScene(string relativePath = "Main.scene")
    {
        var scene = new Scene();
        scene.Add(new GameObject("Root"));
        Guid guid = CreateSceneAsset(scene, relativePath);
        Assert.NotEqual(Guid.Empty, guid);
        return guid;
    }

    /// <summary>Author a scene whose root has the named component from the compiled game assembly.</summary>
    protected Guid AuthorSceneWithComponent(string componentTypeName, string relativePath = "Main.scene")
    {
        var gameAsm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var type = gameAsm.GetType(componentTypeName)
            ?? throw new InvalidOperationException($"Type '{componentTypeName}' not found in the game assembly.");

        var scene = new Scene();
        var go = new GameObject("Root");
        go.AddComponent(type);
        scene.Add(go);
        Guid guid = CreateSceneAsset(scene, relativePath);
        Assert.NotEqual(Guid.Empty, guid);
        return guid;
    }

    /// <summary>Build the project to a fresh temp output directory and assert success. Returns the output path.</summary>
    protected string RunBuild(Guid sceneGuid, AssetPackagingMode packaging = AssetPackagingMode.LooseFiles)
    {
        EditorRegistries.Initialize(); // idempotent - ensures BuildSettings exists

        var build = EditorRegistries.GetSettings<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.PackagingMode = packaging;

        string outDir = Path.Combine(Path.GetTempPath(), "ProwlTestBuildOut", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = outDir;

        var result = new DesktopBuildPipeline().BuildAsync(Project.RootPath, build, outDir).GetAwaiter().GetResult();
        Assert.True(result.Success, $"Build failed: {result.Errors}");
        return result.OutputPath;
    }

    /// <summary>Compile a tiny class library with a given assembly name via `dotnet build`; returns the DLL path.</summary>
    protected static string CompileLibrary(string assemblyName, string code)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ProwlTestLib", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Lib.cs"), code);
        File.WriteAllText(Path.Combine(dir, $"{assemblyName}.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{{assemblyName}}</AssemblyName>
                <Nullable>enable</Nullable>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
            </Project>
            """);

        var (exit, stdout, stderr) = RunDotnet($"build \"{Path.Combine(dir, assemblyName + ".csproj")}\" -c Release", dir);
        string dll = Path.Combine(dir, "bin", "Release", $"{assemblyName}.dll");
        Assert.True(exit == 0 && File.Exists(dll), $"Building library '{assemblyName}' failed:\n{stdout}\n{stderr}");
        return dll;
    }

    /// <summary>Run the built player headlessly for a few frames and return its stdout (asserts a clean exit).</summary>
    protected string RunPlayerHeadless(string outputDir, int frames = 30)
    {
        var build = EditorRegistries.GetSettings<BuildSettings>();
        string exe = new DesktopBuildPipeline().GetExecutablePath(outputDir, build);
        Assert.True(File.Exists(exe), $"Executable not found at {exe}");

        var psi = new ProcessStartInfo(exe, $"--headless --frames {frames} --fps 0")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = outputDir,
        };
        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        bool exited = proc.WaitForExit(90_000);
        if (!exited) { try { proc.Kill(true); } catch { } }

        Assert.True(exited, "Headless player did not exit within the timeout.");

        // The captured streams are the only account of what went wrong inside the player, so a bare exit
        // code assertion here costs an hour of guessing every time a build regresses.
        Assert.True(proc.ExitCode == 0,
            $"Player exited with {proc.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{stderr}");

        return stdout;
    }

    /// <summary>Best-effort recursive delete (used to clean up temp build outputs).</summary>
    protected static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    /// <summary>Run a `dotnet` command and capture its output.</summary>
    protected static (int exit, string stdout, string stderr) RunDotnet(string args, string workingDir)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        string e = p.StandardError.ReadToEnd();
        p.WaitForExit(180_000);
        return (p.ExitCode, o, e);
    }

    public virtual void Dispose()
    {
        // Drop this test's scene so the next one starts from a fresh empty Scene.Current.
        try { Scene.Current.Dispose(); } catch { }

        Assets.Dispose(); // stops the FileSystemWatcher and clears AssetDatabase.Current / Instance
        Project.CloseCurrent();

        Application.IsEditor = _prevIsEditor;
        Application.IsPlaying = _prevIsPlaying;

        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }

        GC.SuppressFinalize(this);
    }
}
