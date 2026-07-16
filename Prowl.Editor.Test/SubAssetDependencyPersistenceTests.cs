// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using ImageMagick;

using Prowl.Editor.Build;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Confirms a sub-asset's own dependency graph entry (e.g. a Sprite's AssetRef to its Texture2D)
/// survives a simulated editor restart, not just the current session - SubAssetEntry persists its
/// own Dependencies and EditorAssetDatabase re-seeds them from the metadata cache on startup.
/// </summary>
[Trait("Category", "Build")]
public class SubAssetDependencyPersistenceTests : EditorTestHarness
{
    private const int TexSize = 4;
    private const byte R = 10, G = 200, B = 90, A = 255;

    [Fact]
    public void SpriteDependency_SurvivesSimulatedEditorRestart()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        string pngPath = AssetAbsolutePath("PersistTexture.png");
        var color = new MagickColor(R, G, B, A);
        using (var image = new MagickImage(color, TexSize, TexSize))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPath);
        }
        Guid texGuid = Assets.ImportFile("PersistTexture.png");
        Assert.NotEqual(Guid.Empty, texGuid);

        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.Single });
        var subAssets = Assets.GetSubAssets(texGuid);
        Assert.True(subAssets.Length > 0, "Expected a Sprite sub-asset after enabling Sprite mode.");
        Guid spriteGuid = subAssets[0].Guid;

        Guid sceneGuid = AuthorSceneReferencingSprite(spriteGuid);

        // Simulate closing and reopening the editor without touching the texture again, so this
        // only exercises what gets restored from the persisted metadata cache.
        ReopenDatabase();

        // Confirm the sub-asset itself still resolves post-reopen.
        var spriteAfterReopen = AssetDatabase.Get(spriteGuid) as Sprite;
        Assert.True(spriteAfterReopen.IsValid(), "Sprite sub-asset itself should still resolve after a reopen.");

        string outputDir = RunBuild(sceneGuid, AssetPackagingMode.LooseFiles);
        try
        {
            string stdout = RunPlayerHeadless(outputDir);

            Assert.Contains("PROWL_SPRITE_CHECK", stdout);
            Assert.Contains("spriteValid=True", stdout);
            Assert.Contains("texValid=True", stdout); // fails if sub-asset dependencies aren't persisted
        }
        finally
        {
            TryDeleteDir(outputDir);
        }
    }

    private Guid AuthorSceneReferencingSprite(Guid spriteGuid)
    {
        File.WriteAllText(AssetAbsolutePath("PersistCheckComponent.cs"), """
            using Prowl.Runtime;
            using Prowl.Runtime.Resources;

            public class PersistCheckComponent : MonoBehaviour
            {
                public AssetRef<Sprite> MySprite;

                public override void Start()
                {
                    MySprite.EnsureLoaded();
                    var sprite = MySprite.Res;
                    sprite?.Texture.EnsureLoaded();
                    var tex = sprite?.Texture.Res;
                    System.Console.WriteLine($"PROWL_SPRITE_CHECK|spriteValid={sprite.IsValid()}|texValid={tex.IsValid()}|width={tex?.Width}|height={tex?.Height}");
                }
            }
            """);

        var compile = Projects.Scripting.ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Script compile failed:\n{compile.Errors}\n{compile.Output}");

        var gameAsm = System.Reflection.Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var compType = gameAsm.GetType("PersistCheckComponent");
        Assert.NotNull(compType);

        var scene = new Scene();
        var go = new GameObject("PersistChecker");
        var comp = go.AddComponent(compType!);
        compType!.GetField("MySprite")!.SetValue(comp, new AssetRef<Sprite>(spriteGuid));
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");
        Assert.NotEqual(Guid.Empty, sceneGuid);
        return sceneGuid;
    }
}
