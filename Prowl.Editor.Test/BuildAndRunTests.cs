// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;

using ImageMagick;

using Prowl.Editor.Build;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// One full end-to-end pipeline test: author a project with a game script and a scene that uses it,
/// compile the script, build a standalone player, then run it headlessly and confirm the compiled
/// game code executed - including a custom texture referenced via a Sprite sub-asset, so a
/// DependenciesOnly build (the default) is proven to carry the Sprite's own Texture2D dependency
/// through to the Player, not just the Sprite itself.
///
/// This is slow (it shells out to `dotnet build` + `dotnet publish`) and needs the .NET SDK, so it is
/// kept as a single opt-in test under the "Build" category.
/// </summary>
[Trait("Category", "Build")]
public class BuildAndRunTests : EditorTestHarness
{
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
            Assert.Equal(0, proc.ExitCode);
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
}
