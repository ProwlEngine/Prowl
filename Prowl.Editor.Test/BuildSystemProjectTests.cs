// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;

using ImageMagick;

using Prowl.Echo;
using Prowl.Editor.Build;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The parts of the build that need a real project: collecting its assets, compiling its scripts,
/// producing a player and running it. Separate from <see cref="BuildSystemTests"/> because every test
/// here constructs an editor and a project on disk.
/// </summary>
[Trait("Category", "Build")]
public class BuildSystemProjectTests : EditorTestHarness
{
    private readonly string _previousTarget;

    public BuildSystemProjectTests()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        _previousTarget = Profile().SelectedTargetId;
    }

    /// <summary>
    /// Build profiles are shared editor state that survives a project being reopened, so a test picking
    /// a target has to put it back. Leaving Linux selected makes every later build in the run produce a
    /// Linux player and fail on the executable name.
    /// </summary>
    public override void Dispose()
    {
        Profile().SelectedTargetId = _previousTarget;
        base.Dispose();
    }

    // ---------------------------------------------------------------- BuildPipelineTests

    // Each build takes a new folder, so building twice into the same place keeps both and touches
    // nothing that was already there.
    [Fact]
    public void EachBuild_TakesItsOwnFolderAndLeavesTheRestAlone()
    {
        var scene = new Scene();
        scene.Add(new GameObject("Root"));
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        string outDir = Path.Combine(Path.GetTempPath(), "ProwlBuildOut", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        string sentinel = Path.Combine(outDir, "important.txt");
        File.WriteAllText(sentinel, "keep me");

        var build = EditorRegistries.GetSettings<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.OutputDirectory = outDir;

        try
        {
            var first = new DesktopBuildPipeline().BuildAsync(Project.RootPath, build, outDir).GetAwaiter().GetResult();
            var second = new DesktopBuildPipeline().BuildAsync(Project.RootPath, build, outDir).GetAwaiter().GetResult();

            Assert.True(first.Success, first.Errors);
            Assert.True(second.Success, second.Errors);

            Assert.NotEqual(first.OutputPath, second.OutputPath);
            Assert.Equal(outDir, Path.GetDirectoryName(first.OutputPath));
            Assert.Equal(outDir, Path.GetDirectoryName(second.OutputPath));
            Assert.EndsWith(" (1)", second.OutputPath);

            Assert.True(File.Exists(sentinel), "Existing files must not be touched.");
        }
        finally { TryDeleteDir(outDir); }
    }

    // Building into the project itself would have the asset database import a whole player.
    [Fact]
    public void UsableOutputRoot_RefusesTheProjectAndItsAssets()
    {
        string root = Project.RootPath;

        Assert.False(DesktopBuildPipeline.IsUsableOutputRoot("", root, root));
        Assert.False(DesktopBuildPipeline.IsUsableOutputRoot("X", root, root));
        Assert.False(DesktopBuildPipeline.IsUsableOutputRoot("X", Path.Combine(root, "Assets"), root));
        Assert.False(DesktopBuildPipeline.IsUsableOutputRoot("X", Path.Combine(root, "Assets", "Sub"), root));

        Assert.True(DesktopBuildPipeline.IsUsableOutputRoot("X", Path.Combine(root, "Builds"), root));
        Assert.True(DesktopBuildPipeline.IsUsableOutputRoot("X", Path.GetTempPath(), root));
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

        var collected = AssetCollector.Collect(EditorAssetBackend.Instance, [sceneGuid], dependenciesOnly: true);

        Assert.Contains(spriteGuid, collected.AllAssets); // sub-asset itself, via parent backfill
        Assert.Contains(texGuidB, collected.AllAssets);   // what the sub-asset ITSELF depends on
    }

    // RunImport's orphaned-sub-asset cleanup (a sub-asset removed on reimport, e.g. turning Sprite
    // mode off) must also drop the dependency graph entry, or a removed sprite's GUID lingers forever.
    [Fact]
    public void ReimportRemovingSubAsset_RemovesSubAssetDependencyGraphEntries()
    {
        string pngPath = AssetAbsolutePath("ReimportTexture.png");
        using (var image = new MagickImage(new MagickColor(1, 2, 3, 255), 4, 4))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPath);
        }
        Guid texGuid = Assets.ImportFile("ReimportTexture.png");
        Assert.NotEqual(Guid.Empty, texGuid);

        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.Single });
        var subAssets = Assets.GetSubAssets(texGuid);
        Assert.True(subAssets.Length > 0);
        Guid spriteGuid = subAssets[0].Guid;

        // Sanity: the sprite's own dependency (on its parent texture) is really there before the sub-asset disappears.
        Assert.NotEmpty(Assets.Dependencies.GetDependencies(spriteGuid));

        // Switching away from Sprite mode makes the sprite sub-asset disappear on reimport.
        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.None });

        Assert.Empty(Assets.Dependencies.GetDependencies(spriteGuid));
        Assert.Empty(Assets.Dependencies.GetDependents(spriteGuid));
    }

    // Editor/-folder assets are excluded from a build by default, but a texture under Editor/ is
    // still a real runtime asset if a scene/component genuinely references it - the dependency must
    // still ship rather than leaving a silently dangling AssetRef.
    [Fact]
    public void Collect_IncludesRuntimeDependencyLivingUnderEditorFolder()
    {
        string pngPath = AssetAbsolutePath("Editor/Icon.png");
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        using (var image = new MagickImage(new MagickColor(5, 6, 7, 255), 4, 4))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPath);
        }
        Guid texGuid = Assets.ImportFile("Editor/Icon.png");
        Assert.NotEqual(Guid.Empty, texGuid);

        File.WriteAllText(AssetAbsolutePath("EditorTexRefComponent.cs"), """
            using Prowl.Runtime;
            using Prowl.Runtime.Resources;

            public class EditorTexRefComponent : MonoBehaviour
            {
                public AssetRef<Texture2D> MyTexture;
            }
            """);
        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Script compile failed:\n{compile.Errors}\n{compile.Output}");

        var gameAsm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var compType = gameAsm.GetType("EditorTexRefComponent");
        Assert.NotNull(compType);

        var scene = new Scene();
        var go = new GameObject("Root");
        var comp = go.AddComponent(compType!);
        compType!.GetField("MyTexture")!.SetValue(comp, new AssetRef<Texture2D>(texGuid));
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        var collected = AssetCollector.Collect(EditorAssetBackend.Instance, [sceneGuid], dependenciesOnly: true);

        Assert.Contains(texGuid, collected.AllAssets);
    }

    // The default: an Editor/-folder asset with nothing depending on it must NOT ship, even in
    // AllAssets mode (which otherwise dumps every tracked entry into the build).
    [Fact]
    public void Collect_ExcludesUnreferencedEditorFolderAsset()
    {
        string pngPath = AssetAbsolutePath("Editor/Unused.png");
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        using (var image = new MagickImage(new MagickColor(8, 9, 10, 255), 4, 4))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPath);
        }
        Guid texGuid = Assets.ImportFile("Editor/Unused.png");
        Assert.NotEqual(Guid.Empty, texGuid);

        Guid sceneGuid = AuthorEmptyScene("Main.scene");

        var collected = AssetCollector.Collect(EditorAssetBackend.Instance, [sceneGuid], dependenciesOnly: false);

        Assert.DoesNotContain(texGuid, collected.AllAssets);
    }

    // A HashSet does not promise an iteration order, so the same content could lay out differently on
    // every build, and a patch would then have to ship bytes that never changed. The manifest is the
    // cheapest place to observe that, since it records every shipped asset in the order it was written.
    [Fact]
    public void GenerateManifest_IsByteIdenticalRegardlessOfSetOrder()
    {
        var guids = Enumerable.Range(0, 64).Select(_ => Guid.NewGuid()).ToArray();
        var resources = guids.Take(8).Select((g, i) => ($"Textures/Asset{i}", g)).ToArray();
        Guid defaultScene = guids[0];

        string first = WriteManifest(guids, resources, defaultScene);
        string second = WriteManifest(guids.Reverse().ToArray(), resources.Reverse().ToArray(), defaultScene);

        Assert.Equal(
            Convert.ToHexString(File.ReadAllBytes(first)),
            Convert.ToHexString(File.ReadAllBytes(second)));
    }

    private string WriteManifest(Guid[] assets, (string Path, Guid Guid)[] resources, Guid defaultScene)
    {
        string path = Path.Combine(Path.GetTempPath(), $"prowl-manifest-{Guid.NewGuid():N}.bin");
        var pipeline = new OrderProbePipeline();
        pipeline.Write(path, new HashSet<Guid>(assets),
            resources.ToDictionary(r => r.Path, r => r.Guid), defaultScene);
        return path;
    }

    // A stage is one operation, so without a per asset check the executor cannot interrupt a large
    // copy at all and Cancel does nothing until the whole stage finishes.
    [Fact]
    public void CopyingAssets_StopsPartWayWhenCancelled()
    {
        string output = Path.Combine(Path.GetTempPath(), $"prowl-copy-{Guid.NewGuid():N}");
        using var source = new CancellationTokenSource();

        var assets = new HashSet<Guid>(Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()));
        var pipeline = new CopyProbePipeline(opened: 20, source);

        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => pipeline.Copy(assets, output, source.Token));

            int written = Directory.GetFiles(output).Length;

            Assert.InRange(written, 1, assets.Count - 1);
            Assert.True(pipeline.Opened < assets.Count, "The copy ran to the end of the set despite being cancelled.");
        }
        finally
        {
            try { Directory.Delete(output, true); } catch { }
        }
    }

    [Fact]
    public void CopyingAssets_WithAnAlreadyCancelledToken_WritesNothing()
    {
        string output = Path.Combine(Path.GetTempPath(), $"prowl-copy-{Guid.NewGuid():N}");
        using var source = new CancellationTokenSource();
        source.Cancel();

        try
        {
            Assert.ThrowsAny<OperationCanceledException>(
                () => new CopyProbePipeline(int.MaxValue, source).Copy([Guid.NewGuid()], output, source.Token));

            Assert.Empty(Directory.GetFiles(output));
        }
        finally
        {
            try { Directory.Delete(output, true); } catch { }
        }
    }

    /// <summary>
    /// Exposes the protected copy, standing in for the imported bytes so no project is needed, and
    /// cancelling once it has handed out <c>opened</c> assets so the check is exercised part way.
    /// </summary>
    private sealed class CopyProbePipeline(int opened, CancellationTokenSource source) : OrderProbePipeline
    {
        public int Opened { get; private set; }

        public HashSet<Guid> Copy(HashSet<Guid> assets, string output, CancellationToken ct)
            => CopyLooseAssets(assets, output, null, ct);

        protected override Stream OpenShippedAsset(Guid guid)
        {
            if (++Opened >= opened) source.Cancel();
            return new MemoryStream([1, 2, 3, 4]);
        }
    }

    /// <summary>Exposes the protected manifest writer; the abstract members are never reached.</summary>
    private class OrderProbePipeline : BuildPipeline
    {
        public void Write(string outputPath, HashSet<Guid> assets, Dictionary<string, Guid> resources, Guid defaultScene)
            => GenerateManifest(outputPath, assets, resources, defaultScene);

        public override string DisplayName => "order-probe";
        public override Task<BuildResult> BuildAsync(string projectPath, BuildSettings settings, string? outputDirectory = null, BuildProgress? progress = null, CancellationToken cancellation = default) => throw new NotSupportedException();
        public override string GetExecutablePath(string outputPath, BuildSettings settings) => throw new NotSupportedException();
        public override StageGraph CreateStageGraph(BuildRequest request) => throw new NotSupportedException();
        public override IAsyncEnumerable<BuildOperation> PlanStageAsync(BuildStage stage, IBuildContext context, CancellationToken ct) => throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- BuildAndRunTests

    private const string Marker = "PROWL_BUILD_SMOKE_OK";
    private const int TexSize = 4;
    private const byte TexR = 10, TexG = 200, TexB = 90, TexA = 255;

    [Fact]
    public void FullPipeline_Compile_Build_RunHeadless()
    {
        // Project settings must be discovered (BuildSettings, etc.) before compiling/building.
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        // 1. Author a real, tiny PNG, then flip it to Sprite mode (Texture Type -> Sprite in the
        //    Inspector) so the importer also emits a Sprite sub-asset wrapping it.
        string pngPath = AssetAbsolutePath("BuildTestTexture.png");
        var color = new MagickColor(TexR, TexG, TexB, TexA);
        using (var image = new MagickImage(color, TexSize, TexSize))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPath);
        }
        Guid texGuid = Assets.ImportFile("BuildTestTexture.png");
        Assert.NotEqual(Guid.Empty, texGuid);

        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.Single });
        var subAssets = Assets.GetSubAssets(texGuid);
        Assert.True(subAssets.Length > 0, "Expected a Sprite sub-asset after enabling Sprite mode.");
        Guid spriteGuid = subAssets[0].Guid;

        // 2. Author a game script (global namespace so its serialized $type is just the simple name)
        //    that logs the build-smoke marker and reports back whether the texture/sprite resolved.
        File.WriteAllText(AssetAbsolutePath("BuildLogComponent.cs"), $$"""
            using Prowl.Runtime;
            using Prowl.Runtime.Resources;

            public class BuildLogComponent : MonoBehaviour
            {
                public AssetRef<Texture2D> MyTexture;
                public AssetRef<Sprite> MySprite;

                public override void Start()
                {
                    System.Console.WriteLine("{{Marker}}");

                    MyTexture.EnsureLoaded();
                    var tex = MyTexture.Res;
                    System.Console.WriteLine($"PROWL_TEXTURE_CHECK|valid={tex.IsValid()}|width={tex?.Width}|height={tex?.Height}");

                    MySprite.EnsureLoaded();
                    var sprite = MySprite.Res;
                    sprite?.Texture.EnsureLoaded();
                    var spriteTex = sprite?.Texture.Res;
                    System.Console.WriteLine($"PROWL_SPRITE_CHECK|spriteValid={sprite.IsValid()}|texValid={spriteTex.IsValid()}|width={spriteTex?.Width}|height={spriteTex?.Height}");
                }
            }
            """);

        // 3. Compile the user scripts into {Project}.Game.dll.
        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Script compile failed:\n{compile.Errors}\n{compile.Output}");
        Assert.True(File.Exists(Project.GameAssemblyPath), "Game assembly was not produced.");

        // 4. Load the compiled assembly by bytes (no file lock, so the build can rebuild it) and grab
        //    the real component type so the authored scene references exactly what the build will ship.
        var gameAsm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var compType = gameAsm.GetType("BuildLogComponent");
        Assert.NotNull(compType);

        // 5. Author a scene that uses the component and save it as an asset.
        var scene = new Scene();
        var go = new GameObject("Logger");
        var comp = go.AddComponent(compType!);
        compType!.GetField("MyTexture")!.SetValue(comp, new AssetRef<Texture2D>(texGuid));
        compType!.GetField("MySprite")!.SetValue(comp, new AssetRef<Sprite>(spriteGuid));
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");
        Assert.NotEqual(Guid.Empty, sceneGuid);

        // 6. Configure the build. AssetMode stays at its default (DependenciesOnly) - the mode the
        //    Sprite sub-asset dependency bug only reproduces under.
        var build = EditorRegistries.GetSettings<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.PackagingMode = AssetPackagingMode.LooseFiles;
        Assert.Equal(AssetExportMode.DependenciesOnly, build.AssetMode);

        string buildOut = Path.Combine(Path.GetTempPath(), "ProwlBuildOut", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = buildOut;

        try
        {
            // 7. Build.
            var pipeline = new DesktopBuildPipeline();
            var result = pipeline.BuildAsync(Project.RootPath, build, buildOut).GetAwaiter().GetResult();
            Assert.True(result.Success, $"Build failed: {result.Errors}");

            string exe = pipeline.GetExecutablePath(result.OutputPath, build);
            Assert.True(File.Exists(exe), $"Expected executable at {exe}");
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "Content", "asset_manifest.bin")),
                "Expected packaged content manifest.");

            // 8. Run the built player headlessly for a few frames and confirm the game code ran, and
            //    that both the plain texture and the Sprite sub-asset's own texture resolved correctly.
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

            // The captured streams are the only account of what went wrong inside the player, so a bare
            // exit code assertion here costs an hour of guessing every time this breaks.
            Assert.True(proc.ExitCode == 0,
                $"Player exited with {proc.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{stderr}");
            Assert.Contains(Marker, stdout);

            Assert.Contains("PROWL_TEXTURE_CHECK", stdout);
            Assert.Contains("valid=True", stdout);

            Assert.Contains("PROWL_SPRITE_CHECK", stdout);
            Assert.Contains("spriteValid=True", stdout);
            Assert.Contains("texValid=True", stdout);

            Assert.Contains($"width={TexSize}", stdout);
            Assert.Contains($"height={TexSize}", stdout);
        }
        finally
        {
            try { if (Directory.Exists(buildOut)) Directory.Delete(buildOut, true); } catch { }
        }
    }

    /// <summary>
    /// An <c>async void</c> method in a game script. Its state machine calls
    /// <c>AsyncVoidMethodBuilder.Create()</c>, which lives in the core library and is reached through
    /// type forwards in the framework facades, so it is the first thing to break when the player loads
    /// those facades from somewhere the runtime did not put them.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsyncVoidInAGameScript_RunsInABuild(bool selfContained)
    {
        File.WriteAllText(AssetAbsolutePath("AsyncVoidComponent.cs"), """
            using Prowl.Runtime;

            public class AsyncVoidComponent : MonoBehaviour
            {
                private static volatile bool s_finished;
                private static bool s_reported;
                private static int s_frames;

                public override void Start()
                {
                    try { Fire(); }
                    catch (System.Exception e)
                    {
                        System.Console.WriteLine("PROWL_ASYNC_FAIL|" + e.GetType().Name + "|" + e.Message);
                    }
                }

                // Waited for across frames rather than by blocking. The engine resumes continuations on
                // the main thread, so sleeping here waiting for one would wait for work only this thread
                // can run.
                public override void Update()
                {
                    if (s_reported) return;

                    if (s_finished)
                    {
                        s_reported = true;
                        System.Console.WriteLine("PROWL_ASYNC_OK");
                        return;
                    }

                    if (++s_frames > 400)
                    {
                        s_reported = true;
                        System.Console.WriteLine("PROWL_ASYNC_TIMEOUT");
                        return;
                    }

                    // Lets real time pass between pumps so the delay can actually elapse.
                    System.Threading.Thread.Sleep(1);
                }

                public async void Fire()
                {
                    await System.Threading.Tasks.Task.Delay(1);
                    s_finished = true;
                }
            }
            """);

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Script compile failed:\n{compile.Errors}\n{compile.Output}");

        var gameAsm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var compType = gameAsm.GetType("AsyncVoidComponent");
        Assert.NotNull(compType);

        var scene = new Scene();
        var go = new GameObject("Async");
        go.AddComponent(compType!);
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        var build = EditorRegistries.GetSettings<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.PackagingMode = AssetPackagingMode.LooseFiles;

        var profile = Profile();
        bool wasSelfContained = profile.SelfContained;
        profile.SelfContained = selfContained;

        string buildOut = Path.Combine(Path.GetTempPath(), "ProwlAsyncVoid", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = buildOut;

        try
        {
            var result = new DesktopBuildPipeline().BuildAsync(Project.RootPath, build, buildOut).GetAwaiter().GetResult();
            Assert.True(result.Success, $"Build failed: {result.Errors}");

            string stdout = RunPlayerHeadless(result.OutputPath, frames: 60);

            Assert.DoesNotContain("PROWL_ASYNC_FAIL", stdout);
            Assert.Contains("PROWL_ASYNC_OK", stdout);
        }
        finally
        {
            profile.SelfContained = wasSelfContained;
            try { if (Directory.Exists(buildOut)) Directory.Delete(buildOut, true); } catch { }
        }
    }

    /// <summary>
    /// A self contained build carries its own copy of the framework in the output root, and the runtime
    /// resolves those itself before any of the player's code runs. Moving one into runtimes/ is a game
    /// that dies on startup with "Could not load file or assembly", which is what a hand written list of
    /// the framework assemblies somebody had seen fail produced.
    /// </summary>
    [Fact]
    public void SelfContainedBuild_KeepsTheFrameworkInTheRootAndRuns()
    {
        var scene = new Scene();
        scene.Add(new GameObject("Root"));
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        var build = EditorRegistries.GetSettings<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.PackagingMode = AssetPackagingMode.LooseFiles;

        var profile = Profile();
        bool wasSelfContained = profile.SelfContained;
        profile.SelfContained = true;

        string buildOut = Path.Combine(Path.GetTempPath(), "ProwlSelfContained", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = buildOut;

        try
        {
            var pipeline = new DesktopBuildPipeline();
            var result = pipeline.BuildAsync(Project.RootPath, build, buildOut).GetAwaiter().GetResult();
            Assert.True(result.Success, $"Build failed: {result.Errors}");

            // The framework is what a self contained publish put in the root, so if any of it is here
            // then all of it has to be.
            var moved = Directory.GetFiles(Path.Combine(result.OutputPath, "runtimes"), "*.dll")
                .Select(Path.GetFileName)
                .Where(f => DesktopBuildPipeline.IsFrameworkAssembly(f!))
                .ToList();

            Assert.True(moved.Count == 0,
                "These belong to the runtime and were moved out of the root: " + string.Join(", ", moved));

            Assert.True(File.Exists(Path.Combine(result.OutputPath, "System.Threading.dll")),
                "A self contained build did not put the framework in its output root.");

            string stdout = RunPlayerHeadless(result.OutputPath, frames: 5);
            Assert.DoesNotContain("Could not load file or assembly", stdout);
        }
        finally
        {
            profile.SelfContained = wasSelfContained;
            try { if (Directory.Exists(buildOut)) Directory.Delete(buildOut, true); } catch { }
        }
    }

    // ---------------------------------------------------------------- AssetVariantWiringTests

    /// <summary>Applies to everything and records what it was asked to do.</summary>
    private sealed class RecordingProcessor : IAssetVariantProcessor
    {
        public int Calls;
        public string Id => "test-recording";
        public int Version => 1;

        // Desktop lists rgba8 among its accepted formats, so this is selectable there.
        public string Format => TextureFormats.Rgba8;

        public bool AppliesTo(AssetEntry asset) => true;

        public Task ProcessAsync(AssetEntry asset, Stream source, Stream destination, PlatformTarget target, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return source.CopyToAsync(destination, ct);
        }
    }

    private Guid CreateScene()
    {
        var scene = new Scene();
        scene.Add(new GameObject("Root"));
        return CreateSceneAsset(scene, "Main.scene");
    }

    private string RunBuildWith(DesktopBuildPipeline pipeline, Guid sceneGuid, out string outDir)
    {
        var build = EditorRegistries.GetSettings<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.PackagingMode = AssetPackagingMode.LooseFiles;

        outDir = Path.Combine(Path.GetTempPath(), "ProwlVariantOut", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = outDir;

        var result = pipeline.BuildAsync(Project.RootPath, build, outDir).GetAwaiter().GetResult();
        Assert.True(result.Success, $"Build failed: {result.Errors}");
        return result.OutputPath;
    }

    // A registered processor has to actually be reached by a real build.
    [Fact]
    public void RegisteredProcessor_IsInvokedAndPopulatesTheCache()
    {
        Guid sceneGuid = CreateScene();

        var pipeline = new DesktopBuildPipeline();
        var processor = new RecordingProcessor();
        pipeline.AssetProcessors.Add(processor);

        string outDir;
        try
        {
            RunBuildWith(pipeline, sceneGuid, out outDir);

            Assert.True(processor.Calls > 0, "A registered processor was never reached by the build.");

            string cacheRoot = Path.Combine(Project.RootPath, "Library", "BuildCache");
            Assert.True(Directory.Exists(cacheRoot), "The build did not populate the variant cache.");
            Assert.NotEmpty(Directory.GetFiles(cacheRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            TryDeleteDir(Path.Combine(Path.GetTempPath(), "ProwlVariantOut"));
        }
    }

    // The second build of unchanged content must reuse rather than reprocess, which is the whole point.
    [Fact]
    public void SecondBuild_ReusesTheCacheInsteadOfReprocessing()
    {
        Guid sceneGuid = CreateScene();

        var first = new DesktopBuildPipeline();
        var firstProcessor = new RecordingProcessor();
        first.AssetProcessors.Add(firstProcessor);
        RunBuildWith(first, sceneGuid, out _);

        Assert.True(firstProcessor.Calls > 0);

        var second = new DesktopBuildPipeline();
        var secondProcessor = new RecordingProcessor();
        second.AssetProcessors.Add(secondProcessor);
        RunBuildWith(second, sceneGuid, out _);

        Assert.Equal(0, secondProcessor.Calls);

        TryDeleteDir(Path.Combine(Path.GetTempPath(), "ProwlVariantOut"));
    }

    /// <summary>Replaces the asset with a marker, so what shipped is unmistakable.</summary>
    private sealed class RewritingProcessor : IAssetVariantProcessor
    {
        public const string Marker = "PROCESSED-BY-TEST";

        public string Id => "test-rewriting";
        public int Version => 1;
        public string Format => TextureFormats.Rgba8;
        public bool AppliesTo(AssetEntry asset) => true;

        public Task ProcessAsync(AssetEntry asset, Stream source, Stream destination, PlatformTarget target, CancellationToken ct)
            => destination.WriteAsync(System.Text.Encoding.UTF8.GetBytes(Marker), ct).AsTask();
    }

    // Processing an asset and then shipping the unprocessed one is the failure this guards against.
    [Fact]
    public void ProcessedBytes_AreWhatShips()
    {
        Guid sceneGuid = CreateScene();

        var pipeline = new DesktopBuildPipeline();
        pipeline.AssetProcessors.Add(new RewritingProcessor());

        try
        {
            string output = RunBuildWith(pipeline, sceneGuid, out _);

            var shipped = Directory.GetFiles(Path.Combine(output, "Content"), "*.asset");
            Assert.NotEmpty(shipped);
            Assert.All(shipped, f => Assert.Equal(RewritingProcessor.Marker, File.ReadAllText(f)));
        }
        finally
        {
            TryDeleteDir(Path.Combine(Path.GetTempPath(), "ProwlVariantOut"));
        }
    }

    // Reusing a pipeline after clearing its processors must not keep serving the earlier variants.
    [Fact]
    public void ClearingTheProcessors_StopsShippingTheirOutput()
    {
        Guid sceneGuid = CreateScene();

        var pipeline = new DesktopBuildPipeline();
        pipeline.AssetProcessors.Add(new RewritingProcessor());

        try
        {
            RunBuildWith(pipeline, sceneGuid, out _);

            pipeline.AssetProcessors.Clear();
            string output = RunBuildWith(pipeline, sceneGuid, out _);

            var shipped = Directory.GetFiles(Path.Combine(output, "Content"), "*.asset");
            Assert.NotEmpty(shipped);
            Assert.All(shipped, f => Assert.NotEqual(RewritingProcessor.Marker, File.ReadAllText(f)));
        }
        finally
        {
            TryDeleteDir(Path.Combine(Path.GetTempPath(), "ProwlVariantOut"));
        }
    }

    // And with none registered, nothing changes: no cache, no cost, today's behaviour exactly.
    [Fact]
    public void NoProcessors_LeavesTheBuildUntouched()
    {
        Guid sceneGuid = CreateScene();

        RunBuildWith(new DesktopBuildPipeline(), sceneGuid, out _);

        string cacheRoot = Path.Combine(Project.RootPath, "Library", "BuildCache");
        Assert.False(Directory.Exists(cacheRoot), "A build with no processors should not create a variant cache.");

        TryDeleteDir(Path.Combine(Path.GetTempPath(), "ProwlVariantOut"));
    }

    // ---------------------------------------------------------------- DesktopTargetValidationTests

    private static DesktopBuildProfile Profile()
        => EditorRegistries.GetSettings<BuildSettings>().GetProfile<DesktopBuildProfile>(typeof(DesktopBuildPipeline));

    private BuildSettings SettingsFor(PlatformTarget target)
    {
        var scene = new Scene();
        scene.Add(new GameObject("Root"));
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        var build = EditorRegistries.GetSettings<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.OutputDirectory = Path.Combine(Path.GetTempPath(), "ProwlTargetOut", Guid.NewGuid().ToString("N"));

        Profile().SelectTarget(target);
        return build;
    }

    // Publish takes one identifier, so shipping the first of two under the target's name would be a lie.
    [Fact]
    public void UniversalTarget_FailsInsteadOfBuildingOneArchitecture()
    {
        var build = SettingsFor(BuiltInTargets.MacOSUniversal);

        var result = new DesktopBuildPipeline()
            .BuildAsync(Project.RootPath, build, build.OutputDirectory).GetAwaiter().GetResult();

        Assert.False(result.Success);
        Assert.Contains("osx-x64", result.Errors);
        Assert.Contains("osx-arm64", result.Errors);

        TryDeleteDir(Path.Combine(Path.GetTempPath(), "ProwlTargetOut"));
    }

    // Build and Run has to know it cannot start a binary this machine does not run.
    [Fact]
    public void CanRunOnHost_IsTrueOnlyForTheHostsOwnPlatform()
    {
        var pipeline = new DesktopBuildPipeline();

        Assert.True(pipeline.CanRunOnHost(SettingsFor(HostTarget())));
        Assert.False(pipeline.CanRunOnHost(SettingsFor(ForeignTarget())));
    }

    private static PlatformTarget HostTarget()
    {
        if (OperatingSystem.IsLinux()) return BuiltInTargets.LinuxX64;
        if (OperatingSystem.IsMacOS()) return BuiltInTargets.MacOSX64;
        return BuiltInTargets.WindowsX64;
    }

    private static PlatformTarget ForeignTarget()
        => OperatingSystem.IsWindows() ? BuiltInTargets.LinuxX64 : BuiltInTargets.WindowsX64;
}
