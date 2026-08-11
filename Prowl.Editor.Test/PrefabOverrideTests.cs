// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Prefabs;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A component with two fields for override detection/application tests.</summary>
public sealed class OverrideComp : MonoBehaviour
{
    public int A;
    public int B;
}

/// <summary>
/// Thorough tests for the prefab override engine in <see cref="PrefabUtility"/>: index-path
/// resolution, nesting roots, comparison-based override detection, apply/revert (whole and single),
/// and refresh-all-instances. The aim is to exercise core flows and flush out edge cases.
/// </summary>
public class PrefabOverrideTests : EditorTestHarness
{
    private Guid MakePrefab(int a, int b, string path)
    {
        var root = new GameObject("Root");
        var c = root.AddComponent<OverrideComp>();
        c.A = a; c.B = b;
        return CreatePrefabAsset(root, path);
    }

    private GameObject Instantiate(Guid guid) => GameObject.InstantiateDetached(GetPrefab(guid)!)!;

    private void SetSceneCurrent(GameObject instance)
    {
        var scene = new Scene();
        scene.Add(instance);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
    }

    // ---------------------------------------------------------------------
    // ---------------------------------------------------------------------
    // Nesting roots
    // ---------------------------------------------------------------------

    [Fact]
    public void InstanceRoot_Detection()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.SetParent(root);
        Guid g = CreatePrefabAsset(root, "P.prefab");

        var instance = Instantiate(g);
        var instChild = instance.Children[0];

        Assert.True(PrefabUtility.IsInstanceRoot(instance));
        Assert.False(PrefabUtility.IsInstanceRoot(instChild));
        Assert.Same(instance, PrefabUtility.GetPrefabInstanceRoot(instChild));
    }

    [Fact]
    public void NestedPrefabRoot_Detection()
    {
        var instance = Instantiate(MakePrefab(1, 1, "P.prefab"));
        // Simulate a nested prefab: a child belonging to a different prefab asset.
        var nested = new GameObject("Nested");
        nested.PrefabAssetId = Guid.NewGuid();
        nested.SetParent(instance);

        Assert.True(PrefabUtility.IsNestedPrefabRoot(nested));
        Assert.True(PrefabUtility.IsInstanceRoot(nested)); // root of its own (different) prefab
        Assert.Same(nested, PrefabUtility.GetPrefabInstanceRoot(nested));
    }

    // ---------------------------------------------------------------------
    // Override detection
    // ---------------------------------------------------------------------

    [Fact]
    public void RecordComponentOverrides_RecordsChangedField()
    {
        var instance = Instantiate(MakePrefab(5, 5, "P.prefab"));
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;

        PrefabUtility.RecordComponentOverrides(instance, comp);

        Assert.True(PrefabUtility.HasAnyOverrides(instance));
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, comp, "A")));
    }

    [Fact]
    public void RecordComponentOverrides_NoChange_NoOverride()
    {
        var instance = Instantiate(MakePrefab(5, 5, "P.prefab"));
        var comp = instance.GetComponent<OverrideComp>()!;

        PrefabUtility.RecordComponentOverrides(instance, comp);

        Assert.False(PrefabUtility.HasAnyOverrides(instance));
    }

    [Fact]
    public void RecordComponentOverrides_RevertingValue_RemovesOverride()
    {
        var instance = Instantiate(MakePrefab(5, 5, "P.prefab"));
        var comp = instance.GetComponent<OverrideComp>()!;

        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, comp, "A")));

        comp.A = 5; // back to source value
        PrefabUtility.RecordComponentOverrides(instance, comp);
        Assert.False(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, comp, "A")));
    }

    [Fact]
    public void RecordGameObjectOverrides_TracksTagIndex()
    {
        var instance = Instantiate(MakePrefab(5, 5, "P.prefab"));
        instance.TagIndex = 3;

        PrefabUtility.RecordGameObjectOverrides(instance);

        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, "TagIndex")));
    }

    [Fact]
    public void RecordGameObjectOverrides_IgnoresName()
    {
        var instance = Instantiate(MakePrefab(5, 5, "P.prefab"));
        instance.Name = "Renamed";

        PrefabUtility.RecordGameObjectOverrides(instance);

        Assert.False(PrefabUtility.HasAnyOverrides(instance)); // name is per-instance, not an override
    }

    // ---------------------------------------------------------------------
    // Apply / revert
    // ---------------------------------------------------------------------

    [Fact]
    public void ApplyOverrides_WritesChangeBackToPrefabSource()
    {
        Guid g = MakePrefab(5, 5, "P.prefab");
        var instance = Instantiate(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);

        PrefabUtility.ApplyOverrides(instance);

        // A freshly instantiated copy now reflects the applied value.
        var fresh = GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!;
        Assert.Equal(99, fresh.GetComponent<OverrideComp>()!.A);
        Assert.False(PrefabUtility.HasAnyOverrides(instance)); // overrides cleared after apply
    }

    [Fact]
    public void RevertOverrides_RestoresInstanceToSource()
    {
        Guid g = MakePrefab(5, 5, "P.prefab");
        var instance = Instantiate(g);
        instance.GetComponent<OverrideComp>()!.A = 99;
        SetSceneCurrent(instance);

        PrefabUtility.RevertOverrides(instance);

        // RevertOverrides swaps in a fresh copy from the prefab; find it in the scene.
        var current = Scene.Current!.RootObjects.First();
        Assert.Equal(5, current.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void RevertSingleOverride_ResetsFieldAndClearsOverride()
    {
        var instance = Instantiate(MakePrefab(5, 5, "P.prefab"));
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);

        PrefabUtility.RevertSingleOverride(instance, PrefabUtility.GetOverridePath(instance, comp, "A"));

        Assert.Equal(5, comp.A);
        Assert.False(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, comp, "A")));
    }

    [Fact]
    public void ApplySingleOverride_UpdatesSourceForThatField()
    {
        Guid g = MakePrefab(5, 5, "P.prefab");
        var instance = Instantiate(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        var ov = instance.PrefabOverrides.First(o => o.Path == PrefabUtility.GetOverridePath(instance, comp, "A"));

        PrefabUtility.ApplySingleOverride(instance, ov.Path);

        var fresh = GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!;
        Assert.Equal(99, fresh.GetComponent<OverrideComp>()!.A);
    }

    // ---------------------------------------------------------------------
    // Refresh all instances
    // ---------------------------------------------------------------------

    [Fact]
    public void RefreshAllInstances_KeepsOverride_PicksUpSourceChange()
    {
        Guid g = MakePrefab(1, 1, "P.prefab");
        var instance = Instantiate(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;                                   // local override on A
        PrefabUtility.RecordComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        // Change the prefab source's B (a non-overridden field) and reimport.
        EditPrefabSource(g, "P.prefab", src => src.GetComponent<OverrideComp>()!.B = 2);

        PrefabUtility.RefreshAllInstances(g);

        var refreshed = Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!;
        Assert.Equal(99, refreshed.A); // override preserved
        Assert.Equal(2, refreshed.B);  // source change picked up
    }

    // ---------------------------------------------------------------------
    // Create / break
    // ---------------------------------------------------------------------

    [Fact]
    public void UnpackPrefabInstance_ClearsPrefabData()
    {
        var instance = Instantiate(MakePrefab(5, 5, "P.prefab"));
        Assert.True(instance.IsPrefabInstance);

        PrefabUtility.UnpackPrefabInstance(instance);

        Assert.False(instance.IsPrefabInstance);
    }
}
