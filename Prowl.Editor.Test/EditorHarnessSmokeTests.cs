// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Projects;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A simple component used to verify component data survives the prefab asset pipeline.</summary>
public sealed class EditorTestComponent : MonoBehaviour
{
    public int Health;
}

/// <summary>
/// Smoke tests proving the headless editor harness works end to end: a throwaway project is created
/// and made active, the asset database is live, and a prefab can be authored, imported, resolved by
/// GUID and instantiated - with no window or graphics device.
/// </summary>
public class EditorHarnessSmokeTests : EditorTestHarness
{
    [Fact]
    public void Project_IsCreatedAndActive()
    {
        Assert.Same(Project, Project.Current);
        Assert.True(Directory.Exists(Project.AssetsPath));
        Assert.True(Directory.Exists(Project.LibraryPath));
    }

    [Fact]
    public void AssetDatabase_IsRegisteredAsCurrent()
    {
        Assert.Same(Assets, AssetDatabase.Current);
    }

    [Fact]
    public void Prefab_AuthoredImportedResolvedAndInstantiated()
    {
        var source = new GameObject("Enemy");
        source.AddComponent<EditorTestComponent>().Health = 50;
        var weapon = new GameObject("Weapon");
        weapon.SetParent(source);

        Guid guid = CreatePrefabAsset(source, "Enemy.prefab");
        Assert.NotEqual(Guid.Empty, guid);

        var prefab = GetPrefab(guid);
        Assert.NotNull(prefab);

        var instance = prefab!.Instantiate();
        Assert.NotNull(instance);
        Assert.Equal("Enemy", instance!.Name);
        Assert.Equal(guid, instance.PrefabAssetId);
        Assert.True(instance.IsPrefabInstance);
        Assert.Single(instance.Children);
        Assert.Equal(50, instance.GetComponent<EditorTestComponent>()!.Health);
    }

    [Fact]
    public void Prefab_FileIsWrittenUnderAssets()
    {
        var source = new GameObject("Thing");
        CreatePrefabAsset(source, "Sub/Thing.prefab");

        Assert.True(File.Exists(AssetAbsolutePath("Sub/Thing.prefab")));
    }
}
