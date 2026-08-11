// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Prefabs;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// An instance may be given components and children of its own. They belong to that instance: they
/// survive a refresh, a save and reload, and a revert, and applying does not push them into the
/// prefab where every other instance would inherit them.
/// <para/>
/// The reverse, deleting something the prefab provides, is deliberately not supported and stays
/// blocked in the editor, because nothing records it and a refresh would put it straight back.
/// </summary>
public class PrefabInstanceAdditionTests : EditorTestHarness
{
    private GameObject Inst(Guid guid) => GameObject.InstantiateDetached(GetPrefab(guid)!)!;

    private Scene LoadSceneWith(params GameObject[] objects)
    {
        var scene = new Scene();
        foreach (var o in objects) scene.Add(o);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        return Scene.Current!;
    }

    private Guid MakePrefab(string path)
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 2;
        child.SetParent(root);
        return CreatePrefabAsset(root, path);
    }

    private static Scene SaveAndReload(Scene scene)
    {
        EchoObject echo = Serializer.Serialize(typeof(object), scene);
        var reloaded = Serializer.Deserialize<Scene>(echo)!;
        Scene.Load(reloaded);
        Scene.ProcessPendingLoad();
        return Scene.Current!;
    }

    private static GameObject InstanceRootOf(Scene scene)
        => scene.AllObjects.First(o => o.IsPrefabInstance && o.Parent == null);

    private static (LinkComp, GameObject) AddOwnContent(GameObject instance)
    {
        LinkComp component = instance.AddComponent<LinkComp>();
        var child = new GameObject("Mine");
        child.SetParent(instance);
        return (component, child);
    }

    [Fact]
    public void AdditionsAreDistinguishedByHavingNoSource()
    {
        Guid guid = MakePrefab("Classify.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        (LinkComp added, GameObject mine) = AddOwnContent(instance);

        Assert.Equal(Guid.Empty, instance.GetComponentSourceIdentifier(added));
        Assert.Equal(Guid.Empty, mine.SourceIdentifier);
        Assert.NotEqual(Guid.Empty, instance.GetComponentSourceIdentifier(instance.GetComponent<OverrideComp>()!));
        Assert.NotEqual(Guid.Empty, instance.Children.Single(c => c.Name == "Child").SourceIdentifier);
    }

    [Fact]
    public void AdditionsSurviveASceneSaveAndReload()
    {
        Guid guid = MakePrefab("RoundTrip.prefab");
        GameObject instance = Inst(guid);
        Scene scene = LoadSceneWith(instance);

        AddOwnContent(instance);

        GameObject live = InstanceRootOf(SaveAndReload(scene));

        Assert.NotNull(live.GetComponent<LinkComp>());
        Assert.Contains(live.Children, c => c.Name == "Mine");

        // The classification has to survive too, or the next refresh treats them as prefab content.
        Assert.Equal(Guid.Empty, live.GetComponentSourceIdentifier(live.GetComponent<LinkComp>()!));
        Assert.Equal(Guid.Empty, live.Children.Single(c => c.Name == "Mine").SourceIdentifier);
    }

    [Fact]
    public void AdditionsSurviveARefreshAfterAReload()
    {
        Guid guid = MakePrefab("ReloadRefresh.prefab");
        GameObject instance = Inst(guid);
        Scene scene = LoadSceneWith(instance);

        AddOwnContent(instance);
        Scene reloaded = SaveAndReload(scene);

        EditPrefabSource(guid, "ReloadRefresh.prefab", src => src.GetComponent<OverrideComp>()!.A = 9);
        PrefabUtility.RefreshAllInstances(guid);

        GameObject live = InstanceRootOf(reloaded);
        Assert.NotNull(live.GetComponent<LinkComp>());
        Assert.Contains(live.Children, c => c.Name == "Mine");
        Assert.Equal(9, live.GetComponent<OverrideComp>()!.A);
        Assert.Equal(2, live.Children.Count);
    }

    [Fact]
    public void ApplyingDoesNotPushAdditionsIntoThePrefab()
    {
        Guid guid = MakePrefab("ApplyAdds.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        AddOwnContent(instance);
        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.ApplyOverrides(instance);

        GameObject asset = Inst(guid);
        Assert.Equal(42, asset.GetComponent<OverrideComp>()!.A);
        Assert.Null(asset.GetComponent<LinkComp>());
        Assert.DoesNotContain(asset.Children, c => c.Name == "Mine");

        // Still the instance's, though.
        Assert.NotNull(instance.GetComponent<LinkComp>());
        Assert.Contains(instance.Children, c => c.Name == "Mine");
    }

    [Fact]
    public void RevertingDoesNotRemoveAdditions()
    {
        Guid guid = MakePrefab("RevertAdds.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        (LinkComp added, _) = AddOwnContent(instance);
        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.RevertOverrides(instance);

        Assert.Equal(1, instance.GetComponent<OverrideComp>()!.A);
        Assert.Same(added, instance.GetComponent<LinkComp>());
        Assert.Contains(instance.Children, c => c.Name == "Mine");
    }

    [Fact]
    public void SourceIdentityIsStableAcrossAReimport()
    {
        Guid guid = MakePrefab("Stable.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        Guid childSource = instance.Children[0].SourceIdentifier;
        OverrideComp component = instance.GetComponent<OverrideComp>()!;
        Guid componentSource = instance.GetComponentSourceIdentifier(component);

        EditPrefabSource(guid, "Stable.prefab", src => src.GetComponent<OverrideComp>()!.A = 5);
        PrefabUtility.RefreshAllInstances(guid);

        Assert.Equal(childSource, instance.Children[0].SourceIdentifier);
        Assert.Equal(componentSource, instance.GetComponentSourceIdentifier(component));
    }

    [Fact]
    public void ANestedInstanceDroppedInByHandIsAnAdditionNotStructure()
    {
        var innerRoot = new GameObject("Inner");
        innerRoot.AddComponent<OverrideComp>().A = 1;
        Guid inner = CreatePrefabAsset(innerRoot, "NestInner.prefab");

        Guid outer = CreatePrefabAsset(new GameObject("Outer"), "NestOuter.prefab");
        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        // Its own prefab gives it a source identifier, which on its own would read as structure.
        GameObject dropped = Inst(inner);
        dropped.SetParent(instance);

        Assert.NotEqual(Guid.Empty, dropped.SourceIdentifier);
        Assert.NotEqual(instance.PrefabAssetId, dropped.PrefabAssetId);
        Assert.False(PrefabUtility.IsProvidedByPrefab(dropped));
    }

    [Fact]
    public void AChildTheOuterPrefabProvidesIsStructure()
    {
        Guid guid = MakePrefab("Structure.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        GameObject provided = instance.Children.Single(c => c.Name == "Child");
        Assert.True(PrefabUtility.IsProvidedByPrefab(provided));

        var mine = new GameObject("Mine");
        mine.SetParent(instance);
        Assert.False(PrefabUtility.IsProvidedByPrefab(mine));
    }
}
