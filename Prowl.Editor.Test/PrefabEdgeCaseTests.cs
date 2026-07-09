// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Prefabs;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A component holding a reference to another component, to probe intra-prefab references.</summary>
public sealed class RefComp : MonoBehaviour
{
    public MonoBehaviour? Other;
}

/// <summary>A component with a vector field, to probe non-scalar override detection.</summary>
public sealed class VecComp : MonoBehaviour
{
    public Float3 V;
}

/// <summary>
/// Edge-case probes for the prefab system - the cases most likely to break: intra-prefab references,
/// non-scalar fields, component-index and deep-child paths, multiple instances, structural drift,
/// and transform/name preservation on revert.
/// </summary>
public class PrefabEdgeCaseTests : EditorTestHarness
{
    private GameObject Instantiate(Guid guid) => GetPrefab(guid)!.Instantiate()!;

    private void SetSceneCurrent(params GameObject[] instances)
    {
        var scene = new Scene();
        foreach (var i in instances) scene.Add(i);
        Scene.Load(scene);
    }

    private void RewritePrefab(string path, GameObject newSource)
        => File.WriteAllText(AssetAbsolutePath(path), Serializer.Serialize(typeof(object), newSource).WriteToString());

    /// <summary>Write a GameObject tree to a .prefab file WITHOUT clearing its prefab data (so a
    /// pre-set nested PrefabAssetId survives), then import it.</summary>
    private Guid WritePrefabRaw(GameObject source, string path)
    {
        File.WriteAllText(AssetAbsolutePath(path), Serializer.Serialize(typeof(object), source).WriteToString());
        return Assets.ImportFile(path);
    }

    // ---------------------------------------------------------------------
    // Systemic breakage: missing / renamed component types ("all prefabs broke")
    // ---------------------------------------------------------------------

    [Fact]
    public void MissingComponentType_KeepsValidComponents_AsPlaceholder()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 7; // valid
        root.AddComponent<VecComp>();            // will be corrupted into a missing type

        string text = Serializer.Serialize(typeof(object), root).WriteToString();
        text = text.Replace("VecComp", "GhostComp_DoesNotExist");
        File.WriteAllText(AssetAbsolutePath("Broken.prefab"), text);
        Guid g = Assets.ImportFile("Broken.prefab");

        var instance = GetPrefab(g)!.Instantiate();

        Assert.NotNull(instance); // the whole prefab does NOT break
        Assert.Equal(7, instance!.GetComponent<OverrideComp>()!.A); // valid component intact
        var comps = instance.GetComponents<MonoBehaviour>().ToList();
        Assert.Contains(comps, c => c is MissingMonobehaviour); // missing one becomes a placeholder
        Assert.DoesNotContain(comps, c => c is VecComp);
    }

    [Fact]
    public void MissingComponentType_SoleComponent_StillInstantiates()
    {
        var root = new GameObject("Root");
        root.AddComponent<VecComp>();

        string text = Serializer.Serialize(typeof(object), root).WriteToString()
            .Replace("VecComp", "GhostComp_DoesNotExist");
        File.WriteAllText(AssetAbsolutePath("Broken2.prefab"), text);
        Guid g = Assets.ImportFile("Broken2.prefab");

        var instance = GetPrefab(g)!.Instantiate();

        Assert.NotNull(instance);
        Assert.Equal("Root", instance!.Name);
        Assert.Contains(instance.GetComponents<MonoBehaviour>(), c => c is MissingMonobehaviour);
    }

    // ---------------------------------------------------------------------
    // Cyclic intra-prefab references
    // ---------------------------------------------------------------------

    [Fact]
    public void CyclicReferences_RewireBothDirections()
    {
        // Two components referencing each other must both rewire to the instance copies (and not
        // infinite-loop). Regression lock for the Components-before-Children deserialize ordering fix.
        var root = new GameObject("Root");
        var r1 = root.AddComponent<RefComp>();
        var child = new GameObject("Child");
        var r2 = child.AddComponent<RefComp>();
        child.SetParent(root);
        r1.Other = r2;
        r2.Other = r1; // cycle

        Guid g = CreatePrefabAsset(root, "Cycle.prefab");
        var instance = Instantiate(g);

        var i1 = instance.GetComponent<RefComp>()!;
        var i2 = instance.Children[0].GetComponent<RefComp>()!;
        Assert.Same(i2, i1.Other);
        Assert.Same(i1, i2.Other);
    }

    // ---------------------------------------------------------------------
    // State / structure preservation on instantiate
    // ---------------------------------------------------------------------

    [Fact]
    public void Instantiate_PreservesDisabledState()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().Enabled = false;
        var child = new GameObject("Child");
        child.SetParent(root);
        child.Enabled = false;

        Guid g = CreatePrefabAsset(root, "Disabled.prefab");
        var instance = Instantiate(g);

        Assert.False(instance.Children[0].Enabled);
        Assert.False(instance.GetComponent<OverrideComp>()!.Enabled);
    }

    [Fact]
    public void EmptyPrefab_Instantiates()
    {
        var instance = Instantiate(CreatePrefabAsset(new GameObject("Empty"), "Empty.prefab"));

        Assert.Equal("Empty", instance.Name);
        Assert.Empty(instance.GetComponents<MonoBehaviour>());
        Assert.Empty(instance.Children);
    }

    [Fact]
    public void ImportFile_Idempotent_KeepsGuidAndResolves()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>();
        Guid g1 = CreatePrefabAsset(root, "Idem.prefab");

        Guid g2 = Assets.ImportFile("Idem.prefab");
        Guid g3 = Assets.ImportFile("Idem.prefab");

        Assert.Equal(g1, g2);
        Assert.Equal(g1, g3);
        Assert.NotNull(GetPrefab(g1));
    }

    [Fact]
    public void NestedPrefab_ThreeLevels_RespectsBoundaries()
    {
        var nestedId = Guid.NewGuid();
        var root = new GameObject("Root");
        var nested = new GameObject("Nested");
        var nestedChild = new GameObject("NestedChild");
        nested.PrefabAssetId = nestedId;
        nestedChild.PrefabAssetId = nestedId; // part of the nested prefab
        nested.SetParent(root);
        nestedChild.SetParent(nested);

        Guid outerId = WritePrefabRaw(root, "Outer.prefab");
        var instance = Instantiate(outerId);

        Assert.Equal(outerId, instance.PrefabAssetId);
        var instNested = instance.Children[0];
        Assert.Equal(nestedId, instNested.PrefabAssetId);          // boundary preserved
        Assert.Equal(nestedId, instNested.Children[0].PrefabAssetId); // not overwritten by outer
    }

    // ---------------------------------------------------------------------
    // Intra-prefab references (the classic hard case)
    // ---------------------------------------------------------------------

    [Fact]
    public void IntraPrefabReference_RewiresToInstanceCopy()
    {
        var root = new GameObject("Root");
        var refComp = root.AddComponent<RefComp>();
        var child = new GameObject("Child");
        var target = child.AddComponent<OverrideComp>();
        child.SetParent(root);
        refComp.Other = target; // points at a component elsewhere in the same prefab

        Guid g = CreatePrefabAsset(root, "Ref.prefab");
        var instance = Instantiate(g);

        var instRef = instance.GetComponent<RefComp>()!;
        var instTarget = instance.Children[0].GetComponent<OverrideComp>()!;

        // The reference must rewire to the INSTANCE's copy, not dangle at the source / null.
        Assert.NotNull(instRef.Other);
        Assert.Same(instTarget, instRef.Other);
    }

    // ---------------------------------------------------------------------
    // Non-scalar field override
    // ---------------------------------------------------------------------

    [Fact]
    public void Float3FieldOverride_DetectedAndApplied()
    {
        var root = new GameObject("Root");
        root.AddComponent<VecComp>().V = new Float3(1, 2, 3);
        Guid g = CreatePrefabAsset(root, "Vec.prefab");

        var instance = Instantiate(g);
        var comp = instance.GetComponent<VecComp>()!;
        comp.V = new Float3(9, 9, 9);

        PrefabUtility.DetectComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, "c0.V"));

        PrefabUtility.ApplyOverrides(instance);
        var fresh = ((PrefabAsset)AssetDatabase.Get(g)!).Instantiate()!;
        Assert.Equal(9.0, fresh.GetComponent<VecComp>()!.V.X, 3);
    }

    // ---------------------------------------------------------------------
    // Component index paths
    // ---------------------------------------------------------------------

    [Fact]
    public void SecondComponent_OverrideByIndex()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;   // c0
        root.AddComponent<VecComp>().V = Float3.Zero; // c1
        Guid g = CreatePrefabAsset(root, "Multi.prefab");

        var instance = Instantiate(g);
        var vec = instance.GetComponents<MonoBehaviour>().ToList()[1] as VecComp;
        vec!.V = new Float3(5, 0, 0);

        PrefabUtility.DetectComponentOverrides(instance, vec);

        Assert.True(PrefabUtility.IsPropertyOverridden(instance, "c1.V"));
    }

    // ---------------------------------------------------------------------
    // Deep child override survives a prefab refresh
    // ---------------------------------------------------------------------

    [Fact]
    public void DeepChildOverride_SurvivesRefresh()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        var grand = new GameObject("Grand");
        var gc = grand.AddComponent<OverrideComp>();
        gc.A = 1; gc.B = 1;
        child.SetParent(root);
        grand.SetParent(child);
        Guid g = CreatePrefabAsset(root, "Deep.prefab");

        var instance = Instantiate(g);
        var instGrandComp = instance.Children[0].Children[0].GetComponent<OverrideComp>()!;
        instGrandComp.A = 99;
        PrefabUtility.DetectComponentOverrides(instance.Children[0].Children[0], instGrandComp);
        SetSceneCurrent(instance);

        // Change the deep child's B in the source.
        var ns = new GameObject("Root");
        var nc = new GameObject("Child");
        var ng = new GameObject("Grand");
        var ngc = ng.AddComponent<OverrideComp>(); ngc.A = 1; ngc.B = 2;
        nc.SetParent(ns); ng.SetParent(nc);
        RewritePrefab("Deep.prefab", ns);
        Assets.Reimport(g);

        PrefabUtility.RefreshAllInstances(g);

        var refreshed = Scene.Current!.RootObjects.First().Children[0].Children[0].GetComponent<OverrideComp>()!;
        Assert.Equal(99, refreshed.A); // override preserved
        Assert.Equal(2, refreshed.B);  // source change picked up
    }

    // ---------------------------------------------------------------------
    // Multiple instances each keep their own overrides through a refresh
    // ---------------------------------------------------------------------

    [Fact]
    public void MultipleInstances_KeepIndependentOverrides_OnRefresh()
    {
        var root = new GameObject("Root");
        var c = root.AddComponent<OverrideComp>(); c.A = 1; c.B = 1;
        Guid g = CreatePrefabAsset(root, "P.prefab");

        var i1 = Instantiate(g);
        var i2 = Instantiate(g);
        i1.GetComponent<OverrideComp>()!.A = 10;
        i2.GetComponent<OverrideComp>()!.A = 20;
        PrefabUtility.DetectComponentOverrides(i1, i1.GetComponent<OverrideComp>()!);
        PrefabUtility.DetectComponentOverrides(i2, i2.GetComponent<OverrideComp>()!);
        SetSceneCurrent(i1, i2);

        var ns = new GameObject("Root");
        var nc = ns.AddComponent<OverrideComp>(); nc.A = 1; nc.B = 2;
        RewritePrefab("P.prefab", ns);
        Assets.Reimport(g);

        PrefabUtility.RefreshAllInstances(g);

        var values = Scene.Current!.RootObjects
            .Select(r => r.GetComponent<OverrideComp>()!)
            .Select(o => (o.A, o.B))
            .OrderBy(t => t.A)
            .ToList();

        Assert.Equal((10, 2), values[0]);
        Assert.Equal((20, 2), values[1]);
    }

    // ---------------------------------------------------------------------
    // Revert preserves per-instance transform + name
    // ---------------------------------------------------------------------

    [Fact]
    public void RevertOverrides_PreservesTransformAndName()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 5;
        Guid g = CreatePrefabAsset(root, "P.prefab");

        var instance = Instantiate(g);
        instance.Transform.Position = new Float3(10, 0, 0);
        instance.Name = "PlacedInstance";
        instance.GetComponent<OverrideComp>()!.A = 99;
        SetSceneCurrent(instance);

        PrefabUtility.RevertOverrides(instance);

        var current = Scene.Current!.RootObjects.First();
        Assert.Equal(5, current.GetComponent<OverrideComp>()!.A); // field reverted
        Assert.Equal("PlacedInstance", current.Name);             // name preserved
        Assert.Equal(10.0, current.Transform.Position.X, 3);      // transform preserved
    }

    // ---------------------------------------------------------------------
    // Structural drift: a component added on the instance shifts indices
    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    // Child overrides are stored on the instance root (regression lock for the
    // "child/grandchild overrides lost on refresh" bug).
    // ---------------------------------------------------------------------

    [Fact]
    public void ChildOverride_IsStoredOnInstanceRoot()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 5;
        child.SetParent(root);
        Guid g = CreatePrefabAsset(root, "C.prefab");

        var instance = Instantiate(g);
        var childGo = instance.Children[0];
        var comp = childGo.GetComponent<OverrideComp>()!;
        comp.A = 99;

        PrefabUtility.DetectComponentOverrides(childGo, comp);

        Assert.Single(instance.PrefabOverrides);   // stored on the root...
        Assert.Empty(childGo.PrefabOverrides);      // ...not on the child
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, "g0.c0.A"));
    }

    [Fact]
    public void ChildOverride_RevertSingle_Works()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 5;
        child.SetParent(root);
        Guid g = CreatePrefabAsset(root, "C.prefab");

        var instance = Instantiate(g);
        var comp = instance.Children[0].GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.DetectComponentOverrides(instance.Children[0], comp);

        PrefabUtility.RevertSingleOverride(instance, "g0.c0.A");

        Assert.Equal(5, comp.A);
        Assert.False(PrefabUtility.HasAnyOverrides(instance));
    }

    [Fact]
    public void StaleOverridePath_IsSkipped_NotMisapplied_OnRefresh()
    {
        // Source has two components; override the second (c1).
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;       // c0
        root.AddComponent<VecComp>().V = Float3.Zero;  // c1
        Guid g = CreatePrefabAsset(root, "Stale.prefab");

        var instance = Instantiate(g);
        var vec = instance.GetComponent<VecComp>()!;
        vec.V = new Float3(7, 0, 0);
        PrefabUtility.DetectComponentOverrides(instance, vec); // records "c1.V"
        SetSceneCurrent(instance);

        // Source structure changes: VecComp removed, so c1 no longer exists.
        var ns = new GameObject("Root");
        ns.AddComponent<OverrideComp>().A = 1;
        RewritePrefab("Stale.prefab", ns);
        Assets.Reimport(g);

        PrefabUtility.RefreshAllInstances(g); // stale override must be skipped, not crash/mis-apply

        var refreshed = Scene.Current!.RootObjects.First();
        Assert.NotNull(refreshed.GetComponent<OverrideComp>());
        Assert.Null(refreshed.GetComponent<VecComp>());
    }

    [Fact]
    public void AddingComponentToInstance_DoesNotCorruptOriginalOverride()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "P.prefab");

        var instance = Instantiate(g);
        // Add a new component BEFORE the original in the list by adding then reordering.
        var added = instance.AddComponent<VecComp>();
        added.SetSiblingIndex(0); // now [VecComp, OverrideComp] -> OverrideComp shifted to c1

        var original = instance.GetComponent<OverrideComp>()!;
        original.A = 42;

        PrefabUtility.DetectComponentOverrides(instance, original);

        // With the index shifted, the source has no component at index 1, so detection BAILS rather
        // than mis-recording the override onto the now-wrong component (the VecComp at c0). The key
        // safety property: no corrupt/misindexed override entry is written. (This is the known
        // index-based-path fragility under structural drift; the apply side is covered by
        // StaleOverridePath_IsSkipped_NotMisapplied_OnRefresh.)
        Assert.Empty(instance.PrefabOverrides);
        Assert.Equal(42, original.A);
    }
}
