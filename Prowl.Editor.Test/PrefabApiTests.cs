// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Prefabs;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The queries and operations tools are expected to build on, rather than each re-deriving them from
/// the instance data.
/// </summary>
public class PrefabApiTests : EditorTestHarness
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

    #region Queries

    [Fact]
    public void IsPartOfPrefabInstance_CoversChildrenNotJustTheRoot()
    {
        Guid guid = MakePrefab("Api_Part.prefab");
        GameObject instance = Inst(guid);
        var outsider = new GameObject("Outsider");
        LoadSceneWith(instance, outsider);

        Assert.True(PrefabUtility.IsPartOfPrefabInstance(instance));
        Assert.True(PrefabUtility.IsPartOfPrefabInstance(instance.Children[0]));
        Assert.False(PrefabUtility.IsPartOfPrefabInstance(outsider));
    }

    [Fact]
    public void FindInstancesOf_ReturnsEveryInstanceRoot()
    {
        Guid guid = MakePrefab("Api_Find.prefab");
        GameObject first = Inst(guid);
        GameObject second = Inst(guid);
        var unrelated = new GameObject("Unrelated");
        LoadSceneWith(first, second, unrelated);

        List<GameObject> found = PrefabUtility.FindInstancesOf(guid);

        Assert.Equal(2, found.Count);
        Assert.Contains(first, found);
        Assert.Contains(second, found);
    }

    [Fact]
    public void FindInstancesOf_DoesNotReturnChildrenOfInstances()
    {
        Guid guid = MakePrefab("Api_FindChild.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        Assert.DoesNotContain(instance.Children[0], PrefabUtility.FindInstancesOf(guid));
    }

    [Fact]
    public void FindInstancesOf_IsEmptyForAnUnknownPrefab()
    {
        MakePrefab("Api_FindNone.prefab");
        LoadSceneWith(new GameObject("Plain"));

        Assert.Empty(PrefabUtility.FindInstancesOf(Guid.NewGuid()));
        Assert.Empty(PrefabUtility.FindInstancesOf(Guid.Empty));
    }

    [Fact]
    public void GetCorrespondingObjectFromSource_FindsTheObjectInThePrefab()
    {
        Guid guid = MakePrefab("Api_Corr.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        GameObject? sourceRoot = PrefabUtility.GetCorrespondingObjectFromSource(instance);
        GameObject? sourceChild = PrefabUtility.GetCorrespondingObjectFromSource(instance.Children[0]);

        Assert.NotNull(sourceRoot);
        Assert.NotNull(sourceChild);

        // The prefab's own objects, not the instance's.
        Assert.NotSame(instance, sourceRoot);
        Assert.NotSame(instance.Children[0], sourceChild);
        Assert.Equal(2, sourceChild!.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void GetCorrespondingObjectFromSource_TracksTheSourceRatherThanTheInstancesValue()
    {
        Guid guid = MakePrefab("Api_CorrValue.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 99;

        // The source still holds what the prefab says, which is the point of asking it.
        Assert.Equal(1, PrefabUtility.GetCorrespondingObjectFromSource(instance)!.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void GetCorrespondingObjectFromSource_FindsTheComponent()
    {
        Guid guid = MakePrefab("Api_CorrComp.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        OverrideComp component = instance.GetComponent<OverrideComp>()!;
        MonoBehaviour? source = PrefabUtility.GetCorrespondingObjectFromSource(component);

        Assert.NotNull(source);
        Assert.NotSame(component, source);
        Assert.Equal(1, ((OverrideComp)source!).A);
    }

    [Fact]
    public void GetCorrespondingObjectFromSource_IsNullForSomethingTheInstanceAdded()
    {
        Guid guid = MakePrefab("Api_CorrAdded.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        LinkComp added = instance.AddComponent<LinkComp>();
        var addedChild = new GameObject("Mine");
        addedChild.SetParent(instance);

        Assert.Null(PrefabUtility.GetCorrespondingObjectFromSource(added));
        Assert.Null(PrefabUtility.GetCorrespondingObjectFromSource(addedChild));
    }

    [Fact]
    public void GetCorrespondingObjectFromSource_IsNullOutsideAnInstance()
    {
        var plain = new GameObject("Plain");
        plain.AddComponent<OverrideComp>();
        LoadSceneWith(plain);

        Assert.Null(PrefabUtility.GetCorrespondingObjectFromSource(plain));
        Assert.Null(PrefabUtility.GetCorrespondingObjectFromSource(plain.GetComponent<OverrideComp>()!));
    }

    #endregion

    #region The override table

    [Fact]
    public void GetPropertyModifications_ReportsTheInstancesOverrides()
    {
        Guid guid = MakePrefab("Api_Get.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        Assert.Single(PrefabUtility.GetPropertyModifications(instance));

        // Asking a child reports the whole instance's set, since that is where they live.
        Assert.Single(PrefabUtility.GetPropertyModifications(instance.Children[0]));
    }

    [Fact]
    public void GetPropertyModifications_HandsBackACopy()
    {
        Guid guid = MakePrefab("Api_Copy.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.GetPropertyModifications(instance).Clear();

        Assert.Single(instance.PrefabOverrides);
    }

    [Fact]
    public void SetPropertyModifications_ClearingThemPutsThePrefabsValuesBack()
    {
        Guid guid = MakePrefab("Api_Set.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        OverrideComp component = instance.GetComponent<OverrideComp>()!;
        component.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.SetPropertyModifications(instance, []);

        Assert.Empty(instance.PrefabOverrides);
        Assert.Same(component, instance.GetComponent<OverrideComp>());
        Assert.Equal(1, component.A);
    }

    [Fact]
    public void SetPropertyModifications_AppliesWhatItIsGiven()
    {
        Guid guid = MakePrefab("Api_SetApply.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);
        List<PropertyOverride> captured = PrefabUtility.GetPropertyModifications(instance);

        // Put it back to the prefab's value, then restore the captured set.
        PrefabUtility.SetPropertyModifications(instance, []);
        Assert.Equal(1, instance.GetComponent<OverrideComp>()!.A);

        PrefabUtility.SetPropertyModifications(instance, captured);
        Assert.Equal(42, instance.GetComponent<OverrideComp>()!.A);
    }

    #endregion

    #region Linking

    [Fact]
    public void ConnectGameObjectToPrefab_RelinksABrokenInstance()
    {
        Guid guid = MakePrefab("Api_Connect.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        PrefabUtility.BreakPrefabInstance(instance);
        Assert.False(instance.IsPrefabInstance);

        Assert.True(PrefabUtility.ConnectGameObjectToPrefab(instance, guid));

        Assert.True(instance.IsPrefabInstance);
        Assert.Equal(guid, instance.PrefabAssetId);
        Assert.NotEqual(Guid.Empty, instance.Children[0].SourceIdentifier);
    }

    [Fact]
    public void ConnectGameObjectToPrefab_TheRelinkedInstanceThenFollowsThePrefab()
    {
        Guid guid = MakePrefab("Api_ConnectFollow.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        PrefabUtility.BreakPrefabInstance(instance);
        PrefabUtility.ConnectGameObjectToPrefab(instance, guid);

        EditPrefabSource(guid, "Api_ConnectFollow.prefab", src => src.GetComponent<OverrideComp>()!.A = 9);
        PrefabUtility.RefreshAllInstances(guid);

        Assert.Equal(9, instance.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ConnectGameObjectToPrefab_RejectsAnUnknownPrefab()
    {
        var plain = new GameObject("Plain");
        LoadSceneWith(plain);

        Assert.False(PrefabUtility.ConnectGameObjectToPrefab(plain, Guid.NewGuid()));
        Assert.False(plain.IsPrefabInstance);
    }

    #endregion

    #region Saving

    [Fact]
    public void SaveAsPrefabAsset_WritesTheAssetWithoutLinkingTheSource()
    {
        var source = new GameObject("Standalone");
        source.AddComponent<OverrideComp>().A = 5;
        LoadSceneWith(source);

        Assert.True(PrefabUtility.SaveAsPrefabAsset(source, "Api_Save.prefab"));

        // The asset exists and holds the content.
        Assets.Refresh();
        var entry = EditorAssetBackend.Instance!.GetEntry("Api_Save.prefab");
        Assert.NotNull(entry);
        Assert.Equal(5, Inst(entry!.Guid).GetComponent<OverrideComp>()!.A);

        // But the object it was written from is not an instance of it.
        Assert.False(source.IsPrefabInstance);
    }

    [Fact]
    public void CreatePrefab_DoesLinkTheSource()
    {
        var source = new GameObject("Connected");
        source.AddComponent<OverrideComp>().A = 5;
        LoadSceneWith(source);

        Assert.True(PrefabUtility.CreatePrefab(source, "Api_Create.prefab"));

        Assert.True(source.IsPrefabInstance);
    }

    #endregion

    #region Events

    [Fact]
    public void OnPrefabSaved_FiresWhenAnAssetIsWritten()
    {
        var seen = new List<Guid>();
        PrefabUtility.OnPrefabSaved += seen.Add;
        try
        {
            var source = new GameObject("Evt");
            source.AddComponent<OverrideComp>().A = 1;
            LoadSceneWith(source);

            Assert.True(PrefabUtility.CreatePrefab(source, "Api_Evt.prefab"));

            Assert.Single(seen);
            Assert.Equal(source.PrefabAssetId, seen[0]);
        }
        finally { PrefabUtility.OnPrefabSaved -= seen.Add; }
    }

    [Fact]
    public void OnPrefabInstanceUpdated_FiresPerInstanceOnRefresh()
    {
        Guid guid = MakePrefab("Api_EvtUpdate.prefab");
        GameObject first = Inst(guid);
        GameObject second = Inst(guid);
        LoadSceneWith(first, second);

        var seen = new List<GameObject>();
        void Handler(GameObject go) => seen.Add(go);

        PrefabUtility.OnPrefabInstanceUpdated += Handler;
        try
        {
            PrefabUtility.RefreshAllInstances(guid);

            Assert.Equal(2, seen.Count);
            Assert.Contains(first, seen);
            Assert.Contains(second, seen);
        }
        finally { PrefabUtility.OnPrefabInstanceUpdated -= Handler; }
    }

    [Fact]
    public void OnPrefabInstantiated_FiresForTheEditorInstantiatePath()
    {
        Guid guid = MakePrefab("Api_EvtInst.prefab");

        var seen = new List<GameObject>();
        void Handler(GameObject go) => seen.Add(go);

        PrefabUtility.OnPrefabInstantiated += Handler;
        try
        {
            GameObject? instance = PrefabUtility.InstantiatePrefab(guid);

            Assert.NotNull(instance);
            Assert.Single(seen);
            Assert.Same(instance, seen[0]);
        }
        finally { PrefabUtility.OnPrefabInstantiated -= Handler; }
    }

    #endregion
}
