// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;

using Prowl.Editor.Build;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// One full end-to-end pipeline test: author a project with a game script and a scene that uses it,
/// compile the script, build a standalone (framework-dependent, Windows), then run the produced player
/// headlessly and confirm the compiled game code actually executed.
///
/// This is slow (it shells out to `dotnet build` + `dotnet publish`) and needs the .NET SDK, so it is
/// kept as a single opt-in test under the "Build" category.
/// </summary>
[Trait("Category", "Build")]
public class BuildAndRunTests : EditorTestHarness
{
    private const string Marker = "PROWL_BUILD_SMOKE_OK";

    [Fact]
    public void FullPipeline_Compile_Build_RunHeadless()
    {
        // Project settings must be discovered (BuildSettings, PackageSettings, etc.) before compiling/building.
        ProjectSettingsRegistry.Initialize();
        ProjectSettingsRegistry.OnProjectOpened();

        // 1. Author a game script (global namespace so its serialized $type is just the simple name).
        File.WriteAllText(AssetAbsolutePath("BuildLogComponent.cs"), $$"""
            using Prowl.Runtime;

            public class BuildLogComponent : MonoBehaviour
            {
                public override void Start()
                {
                    System.Console.WriteLine("{{Marker}}");
                }
            }
            """);

        // 2. Compile the user scripts into {Project}.Game.dll.
        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Script compile failed:\n{compile.Errors}\n{compile.Output}");
        Assert.True(File.Exists(Project.GameAssemblyPath), "Game assembly was not produced.");

        // 3. Load the compiled assembly by bytes (no file lock, so the build can rebuild it) and grab
        //    the real component type so the authored scene references exactly what the build will ship.
        var gameAsm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var compType = gameAsm.GetType("BuildLogComponent");
        Assert.NotNull(compType);

        // 4. Author a scene that uses the component and save it as an asset.
        var scene = new Scene();
        var go = new GameObject("Logger");
        go.AddComponent(compType!);
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");
        Assert.NotEqual(Guid.Empty, sceneGuid);

        // 5. Configure the build: ship the scene, loose-file assets, framework-dependent (profile default).
        var build = ProjectSettingsRegistry.Get<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.PackagingMode = AssetPackagingMode.LooseFiles;

        string buildOut = Path.Combine(Path.GetTempPath(), "ProwlBuildOut", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = buildOut;

        try
        {
            // 6. Build.
            var pipeline = new DesktopBuildPipeline();
            var result = pipeline.BuildAsync(Project.RootPath, build, buildOut).GetAwaiter().GetResult();
            Assert.True(result.Success, $"Build failed: {result.Errors}");

            string exe = pipeline.GetExecutablePath(result.OutputPath, build);
            Assert.True(File.Exists(exe), $"Expected executable at {exe}");
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "Content", "asset_manifest.bin")),
                "Expected packaged content manifest.");

            // 7. Run the built player headlessly for a few frames and confirm the game code ran.
            var psi = new ProcessStartInfo(exe, "--headless --frames 30 --fps 0")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = result.OutputPath,
            };
            using var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            bool exited = proc.WaitForExit(90_000);
            if (!exited) { try { proc.Kill(true); } catch { } }

            Assert.True(exited, "Headless player did not exit within the timeout.");
            Assert.Equal(0, proc.ExitCode);
            Assert.Contains(Marker, stdout);
        }
        finally
        {
            try { if (Directory.Exists(buildOut)) Directory.Delete(buildOut, true); } catch { }
        }
    }
}
