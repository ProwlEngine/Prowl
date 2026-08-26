// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A user-style custom asset: an EngineObject with a create menu entry and plain fields.</summary>
[CreateAssetMenu("Test Custom Asset", Extension = ".customtest", Order = 9000)]
public sealed class CustomTestAsset : EngineObject
{
    public int Rounds = 3;
    public string Label = "unnamed";
    public float Spread = 1.5f;
}

/// <summary>
/// A custom asset has no editor of its own, so the inspector falls back to drawing its serialized
/// fields. These cover what that fallback stands on: no editor is registered, the asset resolves
/// from its guid, and edits written from the inspector survive the round trip to disk.
/// </summary>
public class CustomAssetInspectorTests : EditorTestHarness
{
    private Guid CreateCustomAsset(string path, out CustomTestAsset asset)
    {
        asset = new CustomTestAsset { Name = "Custom" };
        Assets.CreateAsset(asset, path);
        return asset.AssetID;
    }

    [Fact]
    public void CustomAssetHasNoEditorOfItsOwn()
    {
        // The whole reason the inspector needs a field fallback: nothing is registered for a type
        // the engine has never heard of, and its base walk stops at EngineObject.
        Assert.Null(EditorRegistries.GetAssetEditor(typeof(CustomTestAsset)));
    }

    [Fact]
    public void CustomAssetResolvesFromItsEntry()
    {
        Guid guid = CreateCustomAsset("Custom.customtest", out _);

        var entry = Assets.GetEntry("Custom.customtest");
        Assert.NotNull(entry);
        Assert.True(typeof(EngineObject).IsAssignableFrom(entry!.MainAssetType));

        var loaded = AssetDatabase.Get(guid);
        Assert.NotNull(loaded);
        Assert.IsType<CustomTestAsset>(loaded);
    }

    [Fact]
    public void FieldEditsSurviveSaveAndReimport()
    {
        // What the inspector's Save button does: mutate the live instance, write it back, reimport.
        Guid guid = CreateCustomAsset("Custom.customtest", out CustomTestAsset asset);

        asset.Rounds = 42;
        asset.Label = "edited";
        asset.Spread = 0.25f;
        Assets.SaveAsset(asset);

        AssetDatabase.ClearForTests();
        var reloaded = AssetDatabase.Get(guid) as CustomTestAsset;

        Assert.NotNull(reloaded);
        Assert.Equal(42, reloaded!.Rounds);
        Assert.Equal("edited", reloaded.Label);
        Assert.Equal(0.25f, reloaded.Spread, 4);
    }

    [Fact]
    public void ReimportDropsUnsavedFieldEdits()
    {
        // What the Revert button does: throw away edits that were never written.
        Guid guid = CreateCustomAsset("Custom.customtest", out CustomTestAsset asset);
        asset.Rounds = 99;

        Assets.Reimport(guid);
        var reloaded = AssetDatabase.Get(guid) as CustomTestAsset;

        Assert.NotNull(reloaded);
        Assert.Equal(3, reloaded!.Rounds);
    }
}
