// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Build;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Tests for build-pipeline safety and output handling. (The full compile -> build -> run-headless
/// pipeline is covered by <see cref="BuildAndRunTests"/>.)
/// </summary>
public class BuildPipelineTests : EditorTestHarness
{
    public BuildPipelineTests()
    {
        ProjectSettingsRegistry.Initialize();
        ProjectSettingsRegistry.OnProjectOpened();
    }

    // The build never deletes files: it refuses a non-empty output directory, leaving existing data intact.
    [Fact]
    public void Build_RefusesNonEmptyOutputDirectory()
    {
        var scene = new Scene();
        scene.Add(new GameObject("Root"));
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        string outDir = Path.Combine(Path.GetTempPath(), "ProwlBuildOut", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        string sentinel = Path.Combine(outDir, "important.txt");
        File.WriteAllText(sentinel, "keep me");

        var build = ProjectSettingsRegistry.Get<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.OutputDirectory = outDir;

        try
        {
            var result = new DesktopBuildPipeline().BuildAsync(Project.RootPath, build, outDir).GetAwaiter().GetResult();

            Assert.False(result.Success, "Build must refuse a non-empty output directory.");
            Assert.True(File.Exists(sentinel), "Existing files must not be deleted.");
        }
        finally { TryDeleteDir(outDir); }
    }

    [Fact]
    public void IsUsableOutputDirectory_RequiresEmptyOrNew()
    {
        Assert.False(DesktopBuildPipeline.IsUsableOutputDirectory("", Project.RootPath)); // empty path

        string dir = Path.Combine(Path.GetTempPath(), "ProwlUsableOut", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(DesktopBuildPipeline.IsUsableOutputDirectory("X", dir)); // does not exist yet
            Directory.CreateDirectory(dir);
            Assert.True(DesktopBuildPipeline.IsUsableOutputDirectory("X", dir)); // exists but empty
            File.WriteAllText(Path.Combine(dir, "f.txt"), "x");
            Assert.False(DesktopBuildPipeline.IsUsableOutputDirectory("X", dir)); // exists and non-empty
        }
        finally { TryDeleteDir(dir); }
    }
}
