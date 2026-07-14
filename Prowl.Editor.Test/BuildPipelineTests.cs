// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using ImageMagick;

using Prowl.Editor.Build;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects.Scripting;
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

    // Deleting/renaming an asset with sub-assets must also drop the sub-assets' OWN dependency-graph
    // entries, or a deleted Sprite's GUID lingers forever (a leak, and a phantom "Used By" hit).
    [Fact]
    public void DeletingParent_RemovesSubAssetDependencyGraphEntries()
    {
        string pngPath = AssetAbsolutePath("CleanupTexture.png");
        var color = new MagickColor(1, 2, 3, 255);
        using (var image = new MagickImage(color, 4, 4))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPath);
        }
        Guid texGuid = Assets.ImportFile("CleanupTexture.png");
        Assert.NotEqual(Guid.Empty, texGuid);

        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.Single });
        var subAssets = Assets.GetSubAssets(texGuid);
        Assert.True(subAssets.Length > 0);
        Guid spriteGuid = subAssets[0].Guid;

        // Sanity: the sprite's own dependency (on its parent texture) is really there before deleting.
        Assert.NotEmpty(Assets.Dependencies.GetDependencies(spriteGuid));

        Assets.DeleteAsset("CleanupTexture.png");

        Assert.Empty(Assets.Dependencies.GetDependencies(spriteGuid));
        Assert.Empty(Assets.Dependencies.GetDependents(spriteGuid));
    }

    // AssetCollector's sub-asset backfill (a Sprite pulled in only because its parent Texture2D was
    // referenced, not the Sprite itself) must also walk what that sub-asset references, not just add
    // its GUID. Manually seeds the dependency graph since no shipped importer currently produces a
    // sub-asset dependency this specific gap would actually drop.
    [Fact]
    public void Collect_WalksDependenciesOfSubAssetsIncludedOnlyViaParent()
    {
        string pngPathA = AssetAbsolutePath("ParentTextureA.png");
        using (var image = new MagickImage(new MagickColor(10, 20, 30, 255), 4, 4))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPathA);
        }
        Guid texGuidA = Assets.ImportFile("ParentTextureA.png");
        TextureSpriteMeta.Save(texGuidA, new SpriteImportSettings { Mode = SpriteMode.Single });
        Guid spriteGuid = Assets.GetSubAssets(texGuidA)[0].Guid;

        string pngPathB = AssetAbsolutePath("UnrelatedTextureB.png");
        using (var image = new MagickImage(new MagickColor(40, 50, 60, 255), 4, 4))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPathB);
        }
        Guid texGuidB = Assets.ImportFile("UnrelatedTextureB.png");

        // Simulate a sub-asset dependency not covered by any other mechanism: the sprite also
        // depends on texGuidB, on top of whatever it already correctly depends on (its own texture).
        var deps = Assets.Dependencies.GetDependencies(spriteGuid).ToList();
        deps.Add(texGuidB);
        Assets.Dependencies.SetDependencies(spriteGuid, deps);

        // Scene references texture A directly (NOT the sprite) - the sprite is only pulled into the
        // build via the "sub-assets of collected parents" backfill, never via a direct edge.
        File.WriteAllText(AssetAbsolutePath("ParentRefComponent.cs"), """
            using Prowl.Runtime;
            using Prowl.Runtime.Resources;

            public class ParentRefComponent : MonoBehaviour
            {
                public AssetRef<Texture2D> MyTexture;
            }
            """);
        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Script compile failed:\n{compile.Errors}\n{compile.Output}");

        var gameAsm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var compType = gameAsm.GetType("ParentRefComponent");
        Assert.NotNull(compType);

        var scene = new Scene();
        var go = new GameObject("Root");
        var comp = go.AddComponent(compType!);
        compType!.GetField("MyTexture")!.SetValue(comp, new AssetRef<Texture2D>(texGuidA));
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        var collected = AssetCollector.Collect([sceneGuid], dependenciesOnly: true);

        Assert.Contains(spriteGuid, collected.AllAssets); // sub-asset itself, via parent backfill
        Assert.Contains(texGuidB, collected.AllAssets);   // what the sub-asset ITSELF depends on
    }
}
