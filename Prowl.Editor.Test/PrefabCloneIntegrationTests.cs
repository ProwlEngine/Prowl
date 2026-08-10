// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Echo.Cloning;
using Prowl.Editor.GUI;
using Prowl.Editor.Prefabs;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Duplicating, applying, reverting and refreshing all copy object graphs, and all of them have to
/// leave the objects themselves in place while their contents follow the prefab.
/// </summary>
public class PrefabCloneIntegrationTests : EditorTestHarness
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

    /// <summary>Root(OverrideComp A=1) with a Child(OverrideComp A=2).</summary>
    private Guid MakePrefab(string path)
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 2;
        child.SetParent(root);
        return CreatePrefabAsset(root, path);
    }

    #region Duplicating an instance

    [Fact]
    public void DuplicateOfAnInstanceIsItselfAnInstance()
    {
        Guid guid = MakePrefab("Dup.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 99;
        PrefabUtility.ReconcileInstance(instance);

        GameObject dupe = GameObjectClipboard.Duplicate([instance])[0];

        Assert.True(dupe.IsPrefabInstance);
        Assert.Equal(guid, dupe.PrefabAssetId);
        Assert.Equal(99, dupe.GetComponent<OverrideComp>()!.A);
        Assert.Single(dupe.PrefabOverrides);
        Assert.NotSame(instance.PrefabLink, dupe.PrefabLink);
    }

    [Fact]
    public void DuplicatesComponentsResolveBackToThePrefab()
    {
        Guid guid = MakePrefab("DupResolve.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        GameObject dupe = GameObjectClipboard.Duplicate([instance])[0];

        OverrideComp component = dupe.GetComponent<OverrideComp>()!;
        Assert.NotEqual(Guid.Empty, dupe.GetComponentSourceIdentifier(component));
        Assert.NotEqual(Guid.Empty, dupe.Children[0].SourceIdentifier);
    }

    [Fact]
    public void ApplyFromADuplicateReachesTheOtherInstances()
    {
        Guid guid = MakePrefab("ApplyDup.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        GameObject dupe = GameObjectClipboard.Duplicate([instance])[0];
        dupe.GetComponent<OverrideComp>()!.A = 55;
        PrefabUtility.ReconcileInstance(dupe);

        PrefabUtility.ApplyOverrides(dupe);
        PrefabUtility.RefreshAllInstances(guid);

        Assert.Equal(55, instance.GetComponent<OverrideComp>()!.A);
        Assert.Empty(dupe.PrefabOverrides);
    }

    #endregion

    #region Reverting

    [Fact]
    public void RevertKeepsTheObjectAndDropsTheOverrides()
    {
        Guid guid = MakePrefab("Revert.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        OverrideComp component = instance.GetComponent<OverrideComp>()!;
        component.A = 77;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.RevertOverrides(instance);

        Assert.Same(component, instance.GetComponent<OverrideComp>());
        Assert.Equal(1, component.A);
        Assert.Empty(instance.PrefabOverrides);
        Assert.NotNull(instance.Scene);
    }

    [Fact]
    public void RevertOnADuplicateLeavesTheOtherInstanceAlone()
    {
        Guid guid = MakePrefab("RevertDup.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        GameObject dupe = GameObjectClipboard.Duplicate([instance])[0];
        dupe.GetComponent<OverrideComp>()!.A = 77;
        PrefabUtility.ReconcileInstance(dupe);

        PrefabUtility.RevertOverrides(dupe);

        Assert.Equal(1, dupe.GetComponent<OverrideComp>()!.A);
        Assert.Empty(dupe.PrefabOverrides);
        Assert.Equal(1, instance.GetComponent<OverrideComp>()!.A);
    }

    #endregion

    #region Refreshing

    [Fact]
    public void ChildAddedToThePrefabArrivesFullyFormed()
    {
        Guid guid = MakePrefab("Gain.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        EditPrefabSource(guid, "Gain.prefab", src =>
        {
            var extra = new GameObject("Extra");
            extra.AddComponent<OverrideComp>().A = 7;
            extra.SetParent(src);
        });

        PrefabUtility.RefreshAllInstances(guid);

        GameObject added = instance.Children.Single(c => c.Name == "Extra");
        Assert.NotNull(added.Scene);
        Assert.NotEqual(Guid.Empty, added.SourceIdentifier);

        OverrideComp component = added.GetComponent<OverrideComp>()!;
        Assert.Equal(7, component.A);
        Assert.NotEqual(Guid.Empty, added.GetComponentSourceIdentifier(component));
        Assert.Single(added.PrefabLink!.ComponentSources);
    }

    [Fact]
    public void ComponentDroppedByThePrefabIsRemovedCleanly()
    {
        Guid guid = MakePrefab("Drop.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        EditPrefabSource(guid, "Drop.prefab", src =>
        {
            OverrideComp? comp = src.GetComponent<OverrideComp>();
            if (comp != null) src.RemoveComponent(comp);
        });

        PrefabUtility.RefreshAllInstances(guid);

        Assert.Null(instance.GetComponent<OverrideComp>());
        Assert.Empty(instance.PrefabLink!.ComponentSources);
        Assert.Single(instance.Children);
    }

    [Fact]
    public void InstanceAddedComponentKeepsItsSceneReference()
    {
        Guid guid = MakePrefab("SceneRef.prefab");
        GameObject instance = Inst(guid);
        var outsider = new GameObject("Outsider");
        LoadSceneWith(instance, outsider);

        LinkComp added = instance.AddComponent<LinkComp>();
        added.Target = outsider;

        PrefabUtility.RefreshAllInstances(guid);

        Assert.Same(added, instance.GetComponent<LinkComp>());
        Assert.Same(outsider, added.Target);
    }

    [Fact]
    public void RefreshingTwiceChangesNothingTheSecondTime()
    {
        Guid guid = MakePrefab("Twice.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 12;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.RefreshAllInstances(guid);
        int mapSize = instance.PrefabLink!.ComponentSources.Count;

        PrefabUtility.RefreshAllInstances(guid);

        Assert.Equal(12, instance.GetComponent<OverrideComp>()!.A);
        Assert.Equal(mapSize, instance.PrefabLink!.ComponentSources.Count);
        Assert.Single(instance.Children);
    }

    #endregion

    [Fact]
    public void CloningAnInstanceLeavesTheOriginalUntouched()
    {
        Guid guid = MakePrefab("Untouched.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        Guid objectId = instance.Identifier;
        Guid componentId = instance.GetComponent<OverrideComp>()!.Identifier;

        Cloner.Clone(instance);

        Assert.Equal(objectId, instance.Identifier);
        Assert.Equal(componentId, instance.GetComponent<OverrideComp>()!.Identifier);
        Assert.Single(instance.Children);
        Assert.NotNull(instance.Scene);
    }

    #region Duplicating and copying a selection

    [Fact]
    public void DuplicatingTwoObjectsKeepsTheReferenceBetweenThem()
    {
        var a = new GameObject("A");
        var b = new GameObject("B");
        a.AddComponent<LinkComp>().Target = b;
        b.AddComponent<LinkComp>().Target = a;
        LoadSceneWith(a, b);

        List<GameObject> dupes = GameObjectClipboard.Duplicate([a, b]);

        Assert.Equal(2, dupes.Count);
        Assert.Same(dupes[1], dupes[0].GetComponent<LinkComp>()!.Target);
        Assert.Same(dupes[0], dupes[1].GetComponent<LinkComp>()!.Target);
    }

    [Fact]
    public void DuplicatingKeepsAReferenceToSomethingOutsideTheSelection()
    {
        var a = new GameObject("A");
        var outsider = new GameObject("Outsider");
        a.AddComponent<LinkComp>().Target = outsider;
        LoadSceneWith(a, outsider);

        GameObject dupe = GameObjectClipboard.Duplicate([a])[0];

        Assert.Same(outsider, dupe.GetComponent<LinkComp>()!.Target);
    }

    [Fact]
    public void DuplicatingASelectionSkipsChildrenOfSelectedObjects()
    {
        var parent = new GameObject("P");
        var child = new GameObject("C");
        child.SetParent(parent);
        LoadSceneWith(parent);

        List<GameObject> dupes = GameObjectClipboard.Duplicate([parent, child]);

        Assert.Single(dupes);
        Assert.Single(dupes[0].Children);
    }

    [Fact]
    public void CopyLinksSceneReferencesRatherThanCopyingThem()
    {
        var a = new GameObject("A");
        var b = new GameObject("B");
        a.AddComponent<LinkComp>().Target = b;
        LoadSceneWith(a, b);

        // What Copy writes, without going through the system clipboard.
        var write = new SerializationContext { ExternalReferences = SceneReferenceResolver.ForTrees([a]) };
        EchoObject echo = Serializer.Serialize(typeof(object), a, write);

        var read = new SerializationContext { ExternalReferences = new SceneReferenceResolver() };
        GameObject pasted = Serializer.Deserialize<GameObject>(echo, read)!;

        // Not a copy of B that belongs to no scene, but B itself.
        Assert.Same(b, pasted.GetComponent<LinkComp>()!.Target);
    }

    #endregion
}
