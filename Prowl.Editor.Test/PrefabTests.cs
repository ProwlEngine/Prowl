// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo.Cloning;
using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Importers;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Prefabs;
using Prowl.Runtime.Resources;
using Prowl.Runtime;
using Prowl.Vector;
using Xunit;

namespace Prowl.Editor.Test;

#region Test components

public sealed class OverrideComp : MonoBehaviour
{
    public int A;
    public int B;
}

/// <summary>Holds references, for checking they survive and re-point correctly.</summary>
public sealed class LinkComp : MonoBehaviour
{
    public GameObject? Target;
    public MonoBehaviour? Component;
}

public sealed class RefHolderComp : MonoBehaviour
{
    public GameObject? Target;
}

public sealed class RefComp : MonoBehaviour
{
    public MonoBehaviour? Other;
}

public sealed class VecComp : MonoBehaviour
{
    public Float3 V;
}

public sealed class ListComp : MonoBehaviour
{
    public List<int> Values = [];
}

/// <summary>Calls every instance of itself equal, however different they are.</summary>
public sealed class LooseEquality
{
    public int Value;
    public override bool Equals(object? obj) => obj is LooseEquality;
    public override int GetHashCode() => 0;
}

/// <summary>Holds something whose own equality cannot be trusted to mean equal content.</summary>
public sealed class LooseComp : MonoBehaviour
{
    public LooseEquality Held = new();
}

/// <summary>Holds one value a tool bakes per object and one the user authors.</summary>
public sealed class BakedComp : MonoBehaviour
{
    [CloneField(CloneFieldFlags.Skip)] public int BakedValue;
    public int AuthoredValue;
}

/// <summary>State derived in OnValidate, for checking a refresh re-derives it.</summary>
public sealed class DerivedStateComp : MonoBehaviour
{
    public int Source;
    [SerializeIgnore] public int Derived;

    public override void OnValidate() => Derived = Source * 2;
}

#endregion




/// <summary>
/// Everything the prefab system is expected to do, in one place.
/// <para/>
/// The regions below follow the shape of the system rather than the order things were written: what
/// an operation is allowed to touch, how overrides are recorded and resolved, how an instance is
/// brought back into line with its prefab, what belongs to the instance rather than the prefab, and
/// the fact that prefabs do not nest.
/// </summary>
public class PrefabTests : EditorTestHarness
{
    #region Safety

    private GameObject Inst(Guid guid) => GameObject.InstantiateDetached(GetPrefab(guid)!)!;

    private void SetSceneCurrent(params GameObject[] instances)
    {
        var scene = new Scene();
        foreach (var i in instances) scene.Add(i);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
    }

    // ---------------------------------------------------------------------
    // Apply acts on the instance root, not on whatever object it was handed
    // ---------------------------------------------------------------------

    [Fact]
    public void ApplyOverrides_OnChild_DoesNotReplaceAssetWithThatSubtree()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 5;
        child.SetParent(root);
        Guid g = CreatePrefabAsset(root, "Child.prefab");

        var instance = Inst(g);
        var comp = instance.Children[0].GetComponent<OverrideComp>()!;
        comp.A = 42;
        PrefabUtility.RecordComponentOverrides(instance.Children[0], comp);
        SetSceneCurrent(instance);

        PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First().Children[0]);

        var fresh = GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!;
        Assert.Equal("Root", fresh.Name);
        Assert.Single(fresh.Children);
        Assert.Equal(42, fresh.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ApplyOverrides_KeepsSourceNameAndRootTransform()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "Bake.prefab");

        var instance = Inst(g);
        instance.Name = "PlacedInstance";
        instance.Transform.Position = new Float3(10, 20, 30);
        instance.Transform.LocalScale = new Float3(3, 3, 3);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First());

        // Name and placement are per-instance; only the real override reaches the asset.
        var fresh = GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!;
        Assert.Equal("Root", fresh.Name);
        Assert.Equal(0.0, fresh.Transform.Position.X, 3);
        Assert.Equal(1.0, fresh.Transform.LocalScale.X, 3);
        Assert.Equal(99, fresh.GetComponent<OverrideComp>()!.A);
    }

    // ---------------------------------------------------------------------
    // Prefab boundaries: nested instances survive operations on their host
    // ---------------------------------------------------------------------

    [Fact]
    public void CreatePrefab_FlattensNestedPrefabLinks()
    {
        var innerSource = new GameObject("Inner");
        innerSource.AddComponent<OverrideComp>().A = 1;
        Guid inner = CreatePrefabAsset(innerSource, "Inner.prefab");

        var parent = new GameObject("Parent");
        Inst(inner).SetParent(parent);
        SetSceneCurrent(parent);

        Assert.True(PrefabUtility.SaveAsPrefabAssetAndConnect(parent, "Parent.prefab"));

        // Prefabs do not nest. The inner prefab's objects became content of the new one, so the
        // written asset holds no reference back to it.
        Assert.True(parent.IsPrefabInstance);
        Assert.DoesNotContain(inner.ToString(), File.ReadAllText(AssetAbsolutePath("Parent.prefab")));
        Assert.Equal(1, parent.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void UnpackPrefabInstance_LeavesFlattenedContentInPlace()
    {
        Guid inner = CreatePrefabAsset(new GameObject("Inner"), "Inner.prefab");

        var outerSource = new GameObject("Outer");
        Inst(inner).SetParent(outerSource);
        Guid outer = WritePrefabFileRaw(outerSource, "Outer.prefab");

        var instance = Inst(outer);
        SetSceneCurrent(instance);

        PrefabUtility.UnpackPrefabInstance(instance);

        // Prefabs do not nest, so what was once a nested instance is ordinary content by now. Breaking
        // unpacks the whole thing and leaves that content where it is.
        Assert.False(instance.IsPrefabInstance);
        Assert.Single(instance.Children);
        Assert.False(instance.Children[0].IsPrefabInstance);
    }

    // ---------------------------------------------------------------------
    // Break is an instance-level operation
    // ---------------------------------------------------------------------

    [Fact]
    public void UnpackPrefabInstance_OnChild_UnpacksWholeInstanceAndSurvivesRefresh()
    {
        var root = new GameObject("Root");
        new GameObject("Child").SetParent(root);
        Guid g = CreatePrefabAsset(root, "Br.prefab");

        var instance = Inst(g);
        SetSceneCurrent(instance);

        PrefabUtility.UnpackPrefabInstance(instance.Children[0]);

        Assert.False(instance.IsPrefabInstance);
        Assert.False(instance.Children[0].IsPrefabInstance);

        // A refresh must not resurrect the link the user just broke.
        PrefabUtility.RefreshAllInstances(g);
        Assert.False(Scene.Current!.RootObjects.First().IsPrefabInstance);
    }

    // ---------------------------------------------------------------------
    // GameObject-level overrides addressed by property rather than field
    // ---------------------------------------------------------------------

    [Fact]
    public void EnabledOverride_SurvivesRefreshAndCanBeReverted()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "En.prefab");

        var instance = Inst(g);
        instance.Enabled = false;
        PrefabUtility.RecordGameObjectOverrides(instance);
        SetSceneCurrent(instance);

        PrefabUtility.RefreshAllInstances(g);
        var refreshed = Scene.Current!.RootObjects.First();
        Assert.False(refreshed.Enabled);

        PrefabUtility.RevertSingleOverride(refreshed, PrefabUtility.GetOverridePath(refreshed, "Enabled"));
        Assert.True(refreshed.Enabled);
        Assert.False(PrefabUtility.IsPropertyOverridden(refreshed, PrefabUtility.GetOverridePath(refreshed, "Enabled")));
    }

    // ---------------------------------------------------------------------
    // Overridable field set follows serialization, not the inspector
    // ---------------------------------------------------------------------

    [Fact]
    public void ComponentEnabledOverride_SurvivesRefreshAndKeepsHierarchyState()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "CompEn.prefab");

        var instance = Inst(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.Enabled = false;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, comp, "_enabled")));
        SetSceneCurrent(instance);

        PrefabUtility.RefreshAllInstances(g);

        var refreshed = Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!;
        Assert.False(refreshed.Enabled);
        // The raw field write must not leave the component still registered as enabled.
        Assert.False(refreshed.EnabledInHierarchy);
    }

    [Fact]
    public void ComponentIdentifier_IsNeverRecordedAsAnOverride()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "Ident.prefab");

        var instance = Inst(g);
        var comp = instance.GetComponent<OverrideComp>()!;

        // Identifiers are regenerated per deserialization, so instance and source always differ.
        PrefabUtility.RecordComponentOverrides(instance, comp);

        Assert.Empty(instance.PrefabOverrides);
    }

    // ---------------------------------------------------------------------
    // Overridden references link to scene objects instead of cloning them
    // ---------------------------------------------------------------------

    [Fact]
    public void OverriddenSceneReference_RelinksToLiveObject_NotAClone()
    {
        var root = new GameObject("Root");
        root.AddComponent<RefHolderComp>();
        Guid g = CreatePrefabAsset(root, "Ref.prefab");

        var instance = Inst(g);
        var target = new GameObject("SceneTarget");
        SetSceneCurrent(instance, target);

        var comp = instance.GetComponent<RefHolderComp>()!;
        comp.Target = target;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, comp, "Target")));

        PrefabUtility.RefreshAllInstances(g);

        var refreshed = Scene.Current!.RootObjects.First(o => o.Name == "Root").GetComponent<RefHolderComp>()!;
        Assert.Same(target, refreshed.Target);
    }

    [Fact]
    public void ApplyingASceneReference_DoesNotBakeItIntoTheAsset()
    {
        var root = new GameObject("Root");
        root.AddComponent<RefHolderComp>();
        Guid g = CreatePrefabAsset(root, "RefApply.prefab");

        var instance = Inst(g);
        var target = new GameObject("SceneTarget");
        SetSceneCurrent(instance, target);

        var comp = instance.GetComponent<RefHolderComp>()!;
        comp.Target = target;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        var ov = instance.PrefabOverrides.First(o => o.Path == PrefabUtility.GetOverridePath(instance, comp, "Target"));

        PrefabUtility.ApplySingleOverride(instance, ov.Path);

        // A prefab asset cannot hold a scene reference; it must be dropped, not embedded as a copy.
        string text = File.ReadAllText(AssetAbsolutePath("RefApply.prefab"));
        Assert.DoesNotContain("SceneTarget", text);
        Assert.Null(GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!.GetComponent<RefHolderComp>()!.Target);
    }

    [Fact]
    public void CreatePrefab_DoesNotEmbedReferencedSceneObjects()
    {
        var target = new GameObject("ExternalTarget");
        var source = new GameObject("Source");
        source.AddComponent<RefHolderComp>().Target = target;
        SetSceneCurrent(source, target);

        Assert.True(PrefabUtility.SaveAsPrefabAssetAndConnect(source, "Embed.prefab"));

        // The referenced object is not part of the prefab, so it must be linked, not copied in.
        string text = File.ReadAllText(AssetAbsolutePath("Embed.prefab"));
        Assert.DoesNotContain("ExternalTarget", text);
    }

    [Fact]
    public void ApplyOverrides_DoesNotEmbedReferencedSceneObjects()
    {
        var root = new GameObject("Root");
        root.AddComponent<RefHolderComp>();
        Guid g = CreatePrefabAsset(root, "EmbedApply.prefab");

        var instance = Inst(g);
        var target = new GameObject("ExternalTarget");
        SetSceneCurrent(instance, target);

        var comp = instance.GetComponent<RefHolderComp>()!;
        comp.Target = target;
        PrefabUtility.RecordComponentOverrides(instance, comp);

        PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First(o => o.Name == "Root"));

        string text = File.ReadAllText(AssetAbsolutePath("EmbedApply.prefab"));
        Assert.DoesNotContain("ExternalTarget", text);
    }

    // ---------------------------------------------------------------------
    // Undo survives the instance being replaced by a refresh
    // ---------------------------------------------------------------------

    private Guid SetUpOverriddenInstance(string path, out GameObject instance)
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 5;
        Guid g = CreatePrefabAsset(root, path);

        instance = Inst(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        SetSceneCurrent(instance);
        Undo.Clear();
        return g;
    }

    [Fact]
    public void UndoAfterApply_RestoresBothTheAssetAndTheInstanceOverride()
    {
        Guid g = SetUpOverriddenInstance("Undo.prefab", out _);

        PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First());
        Undo.FlushFrame();
        Undo.PerformUndo();

        // The asset goes back to its old value...
        Assert.Equal(5, GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!.GetComponent<OverrideComp>()!.A);

        // ...and the instance keeps the local edit that was applied away.
        var live = Scene.Current!.RootObjects.First();
        Assert.Single(live.PrefabOverrides);
        Assert.Equal(99, live.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ApplyUndoRedoUndo_StaysConsistent()
    {
        Guid g = SetUpOverriddenInstance("UndoRedo.prefab", out _);

        PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First());
        Undo.FlushFrame();

        Undo.PerformUndo();
        Undo.PerformRedo();
        Undo.PerformUndo();

        Assert.Equal(5, GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!.GetComponent<OverrideComp>()!.A);
        var live = Scene.Current!.RootObjects.First();
        Assert.Single(live.PrefabOverrides);
        Assert.Equal(99, live.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void UndoAfterRevert_RestoresTheOverriddenValue()
    {
        SetUpOverriddenInstance("UndoRevert.prefab", out _);

        PrefabUtility.RevertOverrides(Scene.Current!.RootObjects.First());
        Undo.FlushFrame();
        Assert.Equal(5, Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!.A);

        Undo.PerformUndo();
        Assert.Equal(99, Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!.A);

        Undo.PerformRedo();
        Assert.Equal(5, Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!.A);
    }

    // ---------------------------------------------------------------------
    // Applied overrides rebuild derived state
    // ---------------------------------------------------------------------

    [Fact]
    public void AppliedOverride_RunsOnValidateSoDerivedStateMatches()
    {
        var root = new GameObject("Root");
        root.AddComponent<DerivedStateComp>().Source = 1;
        Guid g = CreatePrefabAsset(root, "Derived.prefab");

        var instance = Inst(g);
        var comp = instance.GetComponent<DerivedStateComp>()!;
        comp.Source = 21;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        PrefabUtility.RefreshAllInstances(g);

        var refreshed = Scene.Current!.RootObjects.First().GetComponent<DerivedStateComp>()!;
        Assert.Equal(21, refreshed.Source);
        Assert.Equal(42, refreshed.Derived); // stale would be 2, from the source value
    }

    [Fact]
    public void RevertedOverride_RunsOnValidateSoDerivedStateMatches()
    {
        var root = new GameObject("Root");
        root.AddComponent<DerivedStateComp>().Source = 1;
        Guid g = CreatePrefabAsset(root, "DerivedRevert.prefab");

        var instance = Inst(g);
        var comp = instance.GetComponent<DerivedStateComp>()!;
        comp.Source = 21;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        PrefabUtility.RevertSingleOverride(instance, PrefabUtility.GetOverridePath(instance, comp, "Source"));

        Assert.Equal(1, comp.Source);
        Assert.Equal(2, comp.Derived);
    }

    // ---------------------------------------------------------------------
    // Override detection compares by value, not by identity
    // ---------------------------------------------------------------------

    [Fact]
    public void DetectOverrides_ComparesCollectionsByValue()
    {
        var root = new GameObject("Root");
        root.AddComponent<ListComp>().Values = [1, 2, 3];
        Guid g = CreatePrefabAsset(root, "List.prefab");

        var instance = Inst(g);
        var comp = instance.GetComponent<ListComp>()!;

        // Equal contents in a different list instance must not read as an override.
        comp.Values = [1, 2, 3];
        PrefabUtility.RecordComponentOverrides(instance, comp);
        Assert.Empty(instance.PrefabOverrides);

        comp.Values = [1, 2, 4];
        PrefabUtility.RecordComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, comp, "Values")));
    }

    // ---------------------------------------------------------------------
    // Refresh keeps per-instance state that is not an override
    // ---------------------------------------------------------------------

    [Fact]
    public void Refresh_KeepsHideFlagsAndStaticFlag()
    {
        var root = new GameObject("Root");
        new GameObject("Child").SetParent(root);
        Guid g = CreatePrefabAsset(root, "Flags.prefab");

        var instance = Inst(g);
        instance.HideFlags = HideFlags.NoGizmos;
        instance.Children[0].HideFlags = HideFlags.NoGizmos;
        instance.IsStatic = true;
        PrefabUtility.RecordGameObjectOverrides(instance);
        SetSceneCurrent(instance);

        PrefabUtility.RefreshAllInstances(g);

        var refreshed = Scene.Current!.RootObjects.First();
        Assert.Equal(HideFlags.NoGizmos, refreshed.HideFlags);
        Assert.Equal(HideFlags.NoGizmos, refreshed.Children[0].HideFlags);
        Assert.True(refreshed.IsStatic);
    }

    [Fact]
    public void Refresh_KeepsTheWholeSelection()
    {
        var root = new GameObject("Root");
        new GameObject("Child").SetParent(root);
        Guid g = CreatePrefabAsset(root, "Sel.prefab");

        var a = Inst(g);
        var b = Inst(g);
        var unrelated = new GameObject("Unrelated");
        SetSceneCurrent(a, b, unrelated);

        // Two instances plus a child of one plus an object that is not being refreshed.
        Selection.Clear();
        Selection.AddToSelection(a);
        Selection.AddToSelection(a.Children[0]);
        Selection.AddToSelection(b);
        Selection.AddToSelection(unrelated);

        PrefabUtility.RefreshAllInstances(g);

        var selected = Selection.GetSelected<GameObject>().ToList();
        Assert.Equal(4, selected.Count);
        Assert.All(selected, go => Assert.False(go.IsDisposed));
        Assert.Contains(selected, go => ReferenceEquals(go, unrelated));

        // The refreshed entries are the live replacements, not the objects that were destroyed.
        var live = Scene.Current!.AllObjects.ToList();
        Assert.All(selected, go => Assert.Contains(go, live));
    }

    // ---------------------------------------------------------------------
    // Overrides that no longer resolve can be identified and cleared
    // ---------------------------------------------------------------------

    [Fact]
    public void UnresolvableOverride_IsReportedAndCanBeRemoved()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 1;
        child.SetParent(root);
        Guid g = CreatePrefabAsset(root, "Stale.prefab");

        var instance = Inst(g);
        var comp = instance.Children[0].GetComponent<OverrideComp>()!;
        comp.A = 50;
        PrefabUtility.RecordComponentOverrides(instance.Children[0], comp);
        // Captured now: once the source drops the child there is nothing left to build it from.
        string childPath = PrefabUtility.GetOverridePath(instance.Children[0], comp, "A");
        Assert.True(PrefabUtility.IsOverrideResolvable(instance, childPath));
        SetSceneCurrent(instance);

        // The source drops the child, so the override's path no longer addresses anything.
        File.WriteAllText(AssetAbsolutePath("Stale.prefab"),
            Serializer.Serialize(typeof(object), new GameObject("Root")).WriteToString());
        Assets.Reimport(g);
        PrefabUtility.RefreshAllInstances(g);

        var live = Scene.Current!.RootObjects.First();
        Assert.Single(live.PrefabOverrides);
        Assert.False(PrefabUtility.IsOverrideResolvable(live, childPath));

        PrefabUtility.RemoveOverride(live, childPath);

        Assert.Empty(live.PrefabOverrides);
        Assert.False(PrefabUtility.HasAnyOverrides(live));
    }

    [Fact]
    public void RemoveOverride_IsUndoable()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 5;
        Guid g = CreatePrefabAsset(root, "RemoveUndo.prefab");

        var instance = Inst(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        SetSceneCurrent(instance);
        Undo.Clear();

        PrefabUtility.RemoveOverride(Scene.Current!.RootObjects.First(),
            PrefabUtility.GetOverridePath(instance, comp, "A"));
        Undo.FlushFrame();
        Assert.Empty(Scene.Current!.RootObjects.First().PrefabOverrides);

        Undo.PerformUndo();
        Assert.Single(Scene.Current!.RootObjects.First().PrefabOverrides);
    }

    // ---------------------------------------------------------------------
    // Editor-only prefab bookkeeping does not ship
    // ---------------------------------------------------------------------

    /// <summary>
    /// What a build does with a scene it is shipping: read what was saved, without that scene ever
    /// being the open one.
    /// </summary>
    private static Scene ReadSavedScene(EchoObject saved) => Serializer.Deserialize<Scene>(saved)!;

    [Fact]
    public void RefreshInstancesIn_BringsASceneThatWasNotOpenUpToDate()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "Shelved.prefab");

        EchoObject saved = Serializer.Serialize(typeof(object), LoadSceneWith(Inst(g)));

        // The prefab changes while that scene is not the one open, so what was saved is behind.
        LoadSceneWith(new GameObject("Elsewhere"));
        EditPrefabSource(g, "Shelved.prefab", s => s.GetComponent<OverrideComp>()!.A = 77);

        Scene shipped = ReadSavedScene(saved);
        Assert.Equal(1, shipped.RootObjects.First().GetComponent<OverrideComp>()!.A);

        PrefabUtility.RefreshInstancesIn(shipped);

        Assert.Equal(77, shipped.RootObjects.First().GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void RefreshInstancesIn_KeepsWhatTheInstanceOverrode()
    {
        var root = new GameObject("Root");
        var comp = root.AddComponent<OverrideComp>();
        comp.A = 1; comp.B = 1;
        Guid g = CreatePrefabAsset(root, "ShelvedOv.prefab");

        var instance = Inst(g);
        Scene open = LoadSceneWith(instance);
        instance.GetComponent<OverrideComp>()!.B = 99;
        PrefabUtility.ReconcileInstance(instance);
        EchoObject saved = Serializer.Serialize(typeof(object), open);

        LoadSceneWith(new GameObject("Elsewhere"));
        EditPrefabSource(g, "ShelvedOv.prefab", s => s.GetComponent<OverrideComp>()!.A = 77);

        Scene shipped = ReadSavedScene(saved);
        PrefabUtility.RefreshInstancesIn(shipped);

        var live = shipped.RootObjects.First().GetComponent<OverrideComp>()!;
        Assert.Equal(77, live.A);   // followed the prefab
        Assert.Equal(99, live.B);   // kept its own
    }

    [Fact]
    public void Build_StripsOverrideDataButKeepsThePrefabLink()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 5;
        Guid g = CreatePrefabAsset(root, "Ship.prefab");

        var instance = Inst(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 12345;
        PrefabUtility.RecordComponentOverrides(instance, comp);

        var scene = new Scene();
        scene.Add(instance);
        var echo = Serializer.Serialize(typeof(object), scene);

        Assert.True(Build.BuildPipeline.StripEditorOnlyPrefabData(echo));

        string text = echo.WriteToString();
        Assert.DoesNotContain("Overrides", text);
        Assert.DoesNotContain("SourceComponentCount", text);
        Assert.DoesNotContain("SourceChildCount", text);
        Assert.DoesNotContain("SourceIdentifier", text);
        // Each component records where it came from on itself, and that goes the same way.
        Assert.DoesNotContain("_prefabTemplateIdentity", text);
        // The link itself is observable through IsPrefabInstance, so it stays.
        Assert.Contains("AssetId", text);
        // The overridden value survives as the object's own state, once rather than twice.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "12345"));
    }

    [Fact]
    public void Build_StripReportsNothingToDoOnAPlainScene()
    {
        var scene = new Scene();
        scene.Add(new GameObject("Plain"));

        Assert.False(Build.BuildPipeline.StripEditorOnlyPrefabData(Serializer.Serialize(typeof(object), scene)));
    }

    // ---------------------------------------------------------------------
    // Imported prefabs (models) are revert-only, and track their source file
    // ---------------------------------------------------------------------

    [Fact]
    public void OnlyAuthoredPrefabsAreEditable()
    {
        Guid authored = CreatePrefabAsset(new GameObject("Root"), "Authored.prefab");
        Assert.True(PrefabUtility.IsEditablePrefab(authored));

        // What the model importer produces: same asset type, generated contents.
        GetPrefab(authored)!.InstanceType = PrefabInstanceType.Model;
        Assert.False(PrefabUtility.IsEditablePrefab(authored));

        Assert.False(PrefabUtility.IsEditablePrefab(Guid.NewGuid()));
    }

    [Fact]
    public void GeneratedPrefabs_RefuseApplyButStillRevert()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "Generated.prefab");
        string original = File.ReadAllText(AssetAbsolutePath("Generated.prefab"));

        // Stands in for a model: the asset is rebuilt from a source file, so nothing may be written
        // back to it. For a real model the file would be the .obj/.fbx itself.
        GetPrefab(g)!.InstanceType = PrefabInstanceType.Model;

        var instance = Inst(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        var live = Scene.Current!.RootObjects.First();
        PrefabUtility.ApplyOverrides(live);
        PrefabUtility.ApplySingleOverride(live, live.PrefabOverrides.First().Path);

        // Neither the asset nor its file moved, and the override is still there.
        Assert.Equal(original, File.ReadAllText(AssetAbsolutePath("Generated.prefab")));
        Assert.Single(live.PrefabOverrides);

        // Reverting is the one direction that stays available.
        PrefabUtility.RevertSingleOverride(live, PrefabUtility.GetOverridePath(live, live.GetComponent<OverrideComp>()!, "A"));
        Assert.Equal(1, live.GetComponent<OverrideComp>()!.A);
        Assert.Empty(live.PrefabOverrides);
    }

    [Fact]
    public void ReimportingAPrefab_UpdatesInstancesInTheOpenScene()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "Live.prefab");

        var instance = Inst(g);
        SetSceneCurrent(instance);

        // Stands in for changing a model's import settings: the asset's contents change and it is
        // reimported, with no explicit refresh call from the caller.
        var newSource = new GameObject("Root");
        newSource.AddComponent<OverrideComp>().A = 42;
        File.WriteAllText(AssetAbsolutePath("Live.prefab"),
            Serializer.Serialize(typeof(object), newSource).WriteToString());
        Assets.Reimport(g);

        Assert.Equal(42, Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!.A);
    }

    // ---------------------------------------------------------------------
    // A prefab that changed while the scene was closed
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds an instance of <paramref name="path"/> in a saved scene, changes the prefab while a
    /// different scene is current, and opens the saved scene again.
    /// </summary>
    private GameObject ReopenAfterPrefabChangedWhileClosed(Guid prefabGuid, string path, int newValue)
    {
        var instance = Inst(prefabGuid);
        SetSceneCurrent(instance);
        Guid sceneGuid = CreateSceneAsset(Scene.Current!, "Closed.scene");

        SetSceneCurrent();
        EditPrefabSource(prefabGuid, path, s => s.GetComponent<OverrideComp>()!.A = newValue);

        Scene.Load((Scene)AssetDatabase.Get(sceneGuid)!);
        Scene.ProcessPendingLoad();
        return Scene.Current!.RootObjects.First();
    }

    [Fact]
    public void PrefabChangedWhileTheSceneWasClosed_ReachesTheInstanceOnLoad()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "Closed.prefab");

        var live = ReopenAfterPrefabChangedWhileClosed(g, "Closed.prefab", 77);

        // The import notification only reaches the scene that was open at the time, so opening a
        // scene has to catch it up on whatever changed while it was closed.
        Assert.Equal(77, live.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void PrefabChangedWhileTheSceneWasClosed_IsNotRecordedAsAnOverride()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "ClosedOv.prefab");

        var live = ReopenAfterPrefabChangedWhileClosed(g, "ClosedOv.prefab", 77);

        // What a scene save does before writing. Comparison cannot tell a stale instance from an
        // edited one, so a stale instance here would pin the prefab's own change as an override and
        // freeze every instance against the prefab from then on.
        PrefabUtility.ReconcileInstance(live);
        Assert.Empty(live.PrefabOverrides);
    }

    [Fact]
    public void PrefabChangedWhileTheSceneWasClosed_KeepsTheInstancesOwnOverride()
    {
        var root = new GameObject("Root");
        var comp = root.AddComponent<OverrideComp>();
        comp.A = 1; comp.B = 1;
        Guid g = CreatePrefabAsset(root, "ClosedKeep.prefab");

        var instance = Inst(g);
        var instanceComp = instance.GetComponent<OverrideComp>()!;
        instanceComp.B = 99;
        PrefabUtility.RecordComponentOverrides(instance, instanceComp);
        SetSceneCurrent(instance);
        Guid sceneGuid = CreateSceneAsset(Scene.Current!, "ClosedKeep.scene");

        SetSceneCurrent();
        EditPrefabSource(g, "ClosedKeep.prefab", s => s.GetComponent<OverrideComp>()!.A = 77);

        Scene.Load((Scene)AssetDatabase.Get(sceneGuid)!);
        Scene.ProcessPendingLoad();

        var live = Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!;
        Assert.Equal(77, live.A);   // followed the prefab
        Assert.Equal(99, live.B);   // kept what the instance overrode
    }

    // ---------------------------------------------------------------------
    // A component added to an instance belongs to that instance
    // ---------------------------------------------------------------------

    [Fact]
    public void AddedComponent_SurvivesARefresh()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        var child = new GameObject("Child");
        child.SetParent(root);
        Guid g = CreatePrefabAsset(root, "Added.prefab");

        var instance = Inst(g);
        instance.AddComponent<VecComp>().V = new Float3(5, 6, 7);
        instance.Children[0].AddComponent<VecComp>().V = new Float3(1, 2, 3);
        SetSceneCurrent(instance);

        PrefabUtility.RefreshAllInstances(g);

        var refreshed = Scene.Current!.RootObjects.First();
        var added = refreshed.GetComponent<VecComp>();
        Assert.NotNull(added);
        Assert.Equal(5.0, added!.V.X, 3);

        // Added components on children survive too, not just on the instance root.
        var childAdded = refreshed.Children[0].GetComponent<VecComp>();
        Assert.NotNull(childAdded);
        Assert.Equal(1.0, childAdded!.V.X, 3);

        // The prefab's own component is still there and still the source's.
        Assert.Equal(1, refreshed.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void AddedComponent_SurvivesAReimport()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "AddedReimport.prefab");

        var instance = Inst(g);
        instance.AddComponent<VecComp>().V = new Float3(9, 0, 0);
        SetSceneCurrent(instance);

        // Stands in for changing a model's import settings.
        var newSource = new GameObject("Root");
        newSource.AddComponent<OverrideComp>().A = 5;
        File.WriteAllText(AssetAbsolutePath("AddedReimport.prefab"),
            Serializer.Serialize(typeof(object), newSource).WriteToString());
        Assets.Reimport(g);

        var refreshed = Scene.Current!.RootObjects.First();
        Assert.Equal(5, refreshed.GetComponent<OverrideComp>()!.A);   // source change picked up
        Assert.NotNull(refreshed.GetComponent<VecComp>());            // instance addition kept
    }

    // ---------------------------------------------------------------------
    // Edits are kept whether or not anything asked for them to be detected
    // ---------------------------------------------------------------------

    [Fact]
    public void EditReportedAtTheMoment_SurvivesARefresh()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "Silent.prefab");

        var instance = Inst(g);
        SetSceneCurrent(instance);

        // Straight at the component, as a scene tool or a script would, then reported the way the
        // editor's change hook reports it - no inspector involved.
        var comp = Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!;
        comp.A = 77;
        PrefabUtility.NotifyEdited(comp);

        PrefabUtility.RefreshAllInstances(g);

        Assert.Equal(77, Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ApplyingCarriesEditsNobodyDetected()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "SilentApply.prefab");

        var instance = Inst(g);
        SetSceneCurrent(instance);
        Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!.A = 55;

        PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First());

        Assert.Equal(55, GameObject.InstantiateDetached((PrefabAsset)AssetDatabase.Get(g)!)!
            .GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ReconcileFindsEditsOnChildrenNobodySelected()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 1;
        child.SetParent(root);
        Guid g = CreatePrefabAsset(root, "SilentChild.prefab");

        var instance = Inst(g);
        SetSceneCurrent(instance);

        // Nothing drew this child and nothing reported the edit.
        var live = Scene.Current!.RootObjects.First();
        live.Children[0].GetComponent<OverrideComp>()!.A = 33;
        Assert.Empty(live.PrefabOverrides);

        // A sweep of the whole instance, which is what saving and applying do first.
        PrefabUtility.ReconcileInstance(live);

        Assert.Single(live.PrefabOverrides);
        PrefabUtility.RefreshAllInstances(g);
        Assert.Equal(33, Scene.Current!.RootObjects.First().Children[0].GetComponent<OverrideComp>()!.A);
    }

    // ---------------------------------------------------------------------
    // Play mode never writes to prefab assets
    // ---------------------------------------------------------------------

    [Fact]
    public void PlayMode_BlocksPrefabMutations()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        Guid g = CreatePrefabAsset(root, "Play.prefab");

        var instance = Inst(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 77;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        Application.IsPlaying = true;
        try
        {
            PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First());
            PrefabUtility.UnpackPrefabInstance(Scene.Current!.RootObjects.First());
            Assert.False(PrefabUtility.SaveAsPrefabAssetAndConnect(new GameObject("X"), "X.prefab"));
        }
        finally
        {
            Application.IsPlaying = false;
        }

        Assert.Equal(1, GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!.GetComponent<OverrideComp>()!.A);
        Assert.True(Scene.Current!.RootObjects.First().IsPrefabInstance);
        Assert.False(File.Exists(AssetAbsolutePath("X.prefab")));
    }

    // ---------------------------------------------------------------------
    // CreatePrefab argument validation
    // ---------------------------------------------------------------------

    [Fact]
    public void CreatePrefab_RefusesToClobberAndRejectsNonPrefabPath()
    {
        Assert.True(PrefabUtility.SaveAsPrefabAssetAndConnect(new GameObject("A"), "P.prefab"));

        var b = new GameObject("B");
        Assert.False(PrefabUtility.SaveAsPrefabAssetAndConnect(b, "P.prefab"));
        Assert.True(PrefabUtility.SaveAsPrefabAssetAndConnect(b, "P.prefab", overwrite: true));
        Assert.False(PrefabUtility.SaveAsPrefabAssetAndConnect(b, "NotAPrefab"));
    }

    // ---------------------------------------------------------------------
    // The object left behind by Create Prefab is a working instance of it
    // ---------------------------------------------------------------------

    [Fact]
    public void CreatedInstance_CarriesTheIdentitiesItWasWrittenUnder()
    {
        var root = new GameObject("Root");
        var comp = root.AddComponent<OverrideComp>();
        var child = new GameObject("Child");
        child.SetParent(root);

        CreatePrefabAsset(root, "Made.prefab");

        // An override path names its object and its component by these, so an instance without them
        // addresses nothing in the prefab it was just made from.
        Assert.NotEqual(Guid.Empty, root.SourceIdentifier);
        Assert.NotEqual(Guid.Empty, root.GetComponentSourceIdentifier(comp));
        Assert.NotEqual(Guid.Empty, child.SourceIdentifier);
    }

    [Fact]
    public void CreatedInstance_RecordsAnOverrideOnItsRoot()
    {
        var root = new GameObject("Root");
        var comp = root.AddComponent<OverrideComp>();
        comp.A = 1;
        Guid g = CreatePrefabAsset(root, "MadeOv.prefab");

        comp.A = 42;
        PrefabUtility.RecordComponentOverrides(root, comp);
        root.Enabled = false;
        PrefabUtility.RecordGameObjectOverrides(root);

        Assert.Equal(2, root.PrefabOverrides.Count);

        // And it holds up: the refresh replays recorded overrides and drops everything else.
        PrefabUtility.RefreshAllInstances(g);
        var live = Scene.Current!.RootObjects.First();
        Assert.Equal(42, live.GetComponent<OverrideComp>()!.A);
        Assert.False(live.Enabled);
    }

    [Fact]
    public void CreatedInstance_CanApplyARootEditBackToThePrefab()
    {
        var root = new GameObject("Root");
        var comp = root.AddComponent<OverrideComp>();
        comp.A = 1;
        Guid g = CreatePrefabAsset(root, "MadeApply.prefab");

        comp.A = 42;
        PrefabUtility.RecordComponentOverrides(root, comp);
        Assert.Single(root.PrefabOverrides);

        // Through the single-override path, which has nothing to work from but the entry itself.
        PrefabUtility.ApplySingleOverride(root, root.PrefabOverrides[0].Path);

        Assert.Equal(42, Inst(g).GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void CreatedInstance_CanRevertARootEdit()
    {
        var root = new GameObject("Root");
        var comp = root.AddComponent<OverrideComp>();
        comp.A = 1;
        Guid g = CreatePrefabAsset(root, "MadeRevert.prefab");

        comp.A = 42;
        PrefabUtility.RecordComponentOverrides(root, comp);
        Assert.Single(root.PrefabOverrides);

        PrefabUtility.RevertSingleOverride(root, root.PrefabOverrides[0].Path);

        var live = Scene.Current!.RootObjects.First();
        Assert.Equal(1, live.GetComponent<OverrideComp>()!.A);
        Assert.Empty(live.PrefabOverrides);
    }

    [Fact]
    public void UndoingCreatePrefab_LeavesAPlainObject_AndRedoBringsTheInstanceBack()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>();
        CreatePrefabAsset(root, "MadeUndo.prefab");
        Guid stampedSource = root.SourceIdentifier;
        Assert.NotEqual(Guid.Empty, stampedSource);

        Undo.PerformUndo();
        Assert.False(root.IsPrefabInstance);

        Undo.PerformRedo();
        Assert.True(root.IsPrefabInstance);
        Assert.Equal(stampedSource, root.SourceIdentifier);
    }

    [Fact]
    public void UndoingAnUnpack_RestoresAnInstanceThatStillAddressesItsPrefab()
    {
        var root = new GameObject("Root");
        var comp = root.AddComponent<OverrideComp>();
        comp.A = 1;
        Guid g = CreatePrefabAsset(root, "MadeUnpack.prefab");
        Guid stampedSource = root.SourceIdentifier;

        PrefabUtility.UnpackPrefabInstance(root);
        Assert.False(root.IsPrefabInstance);

        Undo.PerformUndo();

        // Restoring the asset id alone would give back something that reads as an instance but can
        // no longer say which prefab object any of it came from.
        Assert.True(root.IsPrefabInstance);
        Assert.Equal(stampedSource, root.SourceIdentifier);
        Assert.NotEqual(Guid.Empty, root.GetComponentSourceIdentifier(comp));

        comp.A = 42;
        PrefabUtility.RecordComponentOverrides(root, comp);
        Assert.Single(root.PrefabOverrides);
    }

    // ---------------------------------------------------------------------
    // Import rejects files that are not GameObject hierarchies
    // ---------------------------------------------------------------------

    [Fact]
    public void Import_RejectsPrefabThatIsNotAGameObject()
    {
        var notAGameObject = EchoObject.NewCompound();
        notAGameObject.Add("Name", new EchoObject("NotAGameObject"));
        File.WriteAllText(AssetAbsolutePath("Bad.prefab"), notAGameObject.WriteToString());

        Guid g = Assets.ImportFile("Bad.prefab");

        Assert.Null(AssetDatabase.Get(g) as PrefabAsset);
    }

    #endregion

    #region Override

    private Guid MakePrefab(int a, int b, string path)
    {
        var root = new GameObject("Root");
        var c = root.AddComponent<OverrideComp>();
        c.A = a; c.B = b;
        return CreatePrefabAsset(root, path);
    }

    private GameObject Instantiate(Guid guid) => GameObject.InstantiateDetached(GetPrefab(guid)!)!;

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
    public void RecordGameObjectOverrides_TagIndexSurvivesARefresh()
    {
        Guid g = MakePrefab(5, 5, "P.prefab");
        var instance = Instantiate(g);
        SetSceneCurrent(instance);

        instance.TagIndex = 3;
        PrefabUtility.RecordGameObjectOverrides(instance);

        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, "TagIndex")));

        // Recording it is only half of it. A GameObject-level override that is written down and then
        // dropped on the next refresh is how the Enabled override went unnoticed.
        PrefabUtility.RefreshAllInstances(g);
        Assert.Equal(3, instance.TagIndex);
    }

    [Fact]
    public void RecordGameObjectOverrides_IgnoresName()
    {
        Guid g = MakePrefab(5, 5, "P.prefab");
        var instance = Instantiate(g);
        SetSceneCurrent(instance);

        instance.Name = "Renamed";
        PrefabUtility.RecordGameObjectOverrides(instance);

        // A name is per-instance rather than an override, so nothing is recorded and nothing takes it
        // away again either.
        Assert.False(PrefabUtility.HasAnyOverrides(instance));

        PrefabUtility.RefreshAllInstances(g);
        Assert.Equal("Renamed", instance.Name);
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

    #endregion

    #region EdgeCase

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

        var instance = GameObject.InstantiateDetached(GetPrefab(g)!);

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

        var instance = GameObject.InstantiateDetached(GetPrefab(g)!);

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
    public void NestedPrefab_IsFlattenedIntoTheOuterOnImport()
    {
        var nestedId = Guid.NewGuid();
        var root = new GameObject("Root");
        var nested = new GameObject("Nested");
        var nestedChild = new GameObject("NestedChild");
        nested.PrefabAssetId = nestedId;
        nestedChild.PrefabAssetId = nestedId;
        nested.SetParent(root);
        nestedChild.SetParent(nested);

        Guid outerId = WritePrefabFileRaw(root, "Outer.prefab");
        var instance = Instantiate(outerId);

        // An asset written before flattening was enforced loads as one tree: the whole hierarchy is
        // the outer prefab's, with nothing pointing back at the prefab it was nested from.
        Assert.Equal(outerId, instance.PrefabAssetId);
        Assert.Equal(outerId, instance.Children[0].PrefabAssetId);
        Assert.Equal(outerId, instance.Children[0].Children[0].PrefabAssetId);
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

        PrefabUtility.RecordComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, comp, "V")));

        PrefabUtility.ApplyOverrides(instance);
        var fresh = GameObject.InstantiateDetached(((PrefabAsset)AssetDatabase.Get(g)!))!;
        Assert.Equal(9.0, fresh.GetComponent<VecComp>()!.V.X, 3);
    }

    // ---------------------------------------------------------------------
    // Component index paths
    // ---------------------------------------------------------------------

    [Fact]
    public void OverrideOnOneOfSeveralComponents_AppliesToThatOneOnly()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 1;
        root.AddComponent<VecComp>().V = Float3.Zero;
        Guid g = CreatePrefabAsset(root, "Multi.prefab");

        var instance = Instantiate(g);
        SetSceneCurrent(instance);

        var vec = instance.GetComponent<VecComp>()!;
        vec.V = new Float3(5, 0, 0);
        PrefabUtility.RecordComponentOverrides(instance, vec);

        PrefabUtility.RefreshAllInstances(g);

        // The override has to survive and land on the component it was recorded against, leaving the
        // other one following the prefab.
        Assert.Equal(new Float3(5, 0, 0), instance.GetComponent<VecComp>()!.V);
        Assert.Equal(1, instance.GetComponent<OverrideComp>()!.A);
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
        PrefabUtility.RecordComponentOverrides(instance.Children[0].Children[0], instGrandComp);
        SetSceneCurrent(instance);

        // Change the deep child's B in the source.
        EditPrefabSource(g, "Deep.prefab", src => src.Children[0].Children[0].GetComponent<OverrideComp>()!.B = 2);

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
        PrefabUtility.RecordComponentOverrides(i1, i1.GetComponent<OverrideComp>()!);
        PrefabUtility.RecordComponentOverrides(i2, i2.GetComponent<OverrideComp>()!);
        SetSceneCurrent(i1, i2);

        EditPrefabSource(g, "P.prefab", src => src.GetComponent<OverrideComp>()!.B = 2);

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
    // Below the root, where an object sits and what it is called is the prefab's
    // ---------------------------------------------------------------------

    [Fact]
    public void MovingAChild_IsRecordedAndSurvivesARefresh()
    {
        Guid g = MakeNestedPrefab("ChildMove.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.Children[0].Transform.LocalPosition = new Float3(5, 0, 0);
        PrefabUtility.ReconcileInstance(instance);

        Assert.Contains(instance.PrefabOverrides, o => o.Path.EndsWith("/$/Transform.LocalPosition"));

        PrefabUtility.RefreshAllInstances(g);
        Assert.Equal(5.0, instance.Children[0].Transform.LocalPosition.X, 3);
    }

    [Fact]
    public void MovingAChild_CanBeReverted()
    {
        Guid g = MakeNestedPrefab("ChildRevert.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.Children[0].Transform.LocalPosition = new Float3(5, 0, 0);
        PrefabUtility.ReconcileInstance(instance);
        PrefabUtility.RevertOverrides(instance);

        var live = Scene.Current!.RootObjects.First();
        Assert.Equal(0.0, live.Children[0].Transform.LocalPosition.X, 3);
        Assert.Empty(live.PrefabOverrides);
    }

    [Fact]
    public void ThePrefabCanMoveItsOwnChild()
    {
        Guid g = MakeNestedPrefab("ChildLayout.prefab");
        LoadSceneWith(Inst(g));

        EditPrefabSource(g, "ChildLayout.prefab",
            s => s.Children[0].Transform.LocalPosition = new Float3(0, 3, 0));

        // A prefab that could not change its own layout could not be changed at all.
        Assert.Equal(3.0, Scene.Current!.RootObjects.First().Children[0].Transform.LocalPosition.Y, 3);
    }

    [Fact]
    public void AChildTheInstanceMoved_KeepsItsPlaceWhileTheRestFollowsThePrefab()
    {
        Guid g = MakeNestedPrefab("ChildMix.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.Children[0].Transform.LocalPosition = new Float3(5, 0, 0);
        PrefabUtility.ReconcileInstance(instance);

        EditPrefabSource(g, "ChildMix.prefab", s =>
        {
            s.Children[0].Transform.LocalPosition = new Float3(0, 9, 0);
            s.Children[0].Transform.LocalScale = new Float3(2, 2, 2);
        });

        var child = Scene.Current!.RootObjects.First().Children[0];
        Assert.Equal(5.0, child.Transform.LocalPosition.X, 3);  // what the instance said
        Assert.Equal(2.0, child.Transform.LocalScale.X, 3);     // what the prefab said
    }

    [Fact]
    public void OneInstanceMovingAChild_LeavesTheOtherAlone()
    {
        Guid g = MakeNestedPrefab("ChildAlone.prefab");
        var a = Inst(g);
        var b = Inst(g);
        LoadSceneWith(a, b);

        a.Children[0].Transform.LocalPosition = new Float3(5, 0, 0);
        PrefabUtility.ReconcileInstance(a);
        PrefabUtility.RefreshAllInstances(g);

        Assert.Equal(5.0, a.Children[0].Transform.LocalPosition.X, 3);
        Assert.Equal(0.0, b.Children[0].Transform.LocalPosition.X, 3);

        // The move belongs to the instance that made it, and to no other.
        Assert.Contains(a.PrefabOverrides, o => o.Path.EndsWith("/$/Transform.LocalPosition"));
        Assert.Empty(b.PrefabOverrides);
    }

    [Fact]
    public void RenamingAChild_IsRecorded_WhileTheRootsOwnNameIsNot()
    {
        Guid g = MakeNestedPrefab("ChildName.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.Name = "Placed";
        instance.Children[0].Name = "Renamed";
        PrefabUtility.ReconcileInstance(instance);

        // One name override, the child's. Naming the instance is placing it, not editing the prefab.
        Assert.Single(instance.PrefabOverrides.Where(o => o.Path.EndsWith("/$/Name")));

        PrefabUtility.RefreshAllInstances(g);
        var live = Scene.Current!.RootObjects.First();
        Assert.Equal("Placed", live.Name);
        Assert.Equal("Renamed", live.Children[0].Name);
    }

    [Fact]
    public void ThePrefabCanRenameItsOwnChild()
    {
        Guid g = MakeNestedPrefab("ChildRename.prefab");
        LoadSceneWith(Inst(g));

        EditPrefabSource(g, "ChildRename.prefab", s => s.Children[0].Name = "NewName");

        Assert.Equal("NewName", Scene.Current!.RootObjects.First().Children[0].Name);
    }

    // ---------------------------------------------------------------------
    // Overrides read as something a person can act on
    // ---------------------------------------------------------------------

    [Fact]
    public void DescribeOverrides_NamesTheObjectComponentAndMember()
    {
        Guid g = MakeNestedPrefab("Describe.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.Children[0].GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        var described = Assert.Single(PrefabUtility.DescribeOverrides(instance));
        Assert.Equal("Child", described.ObjectName);
        Assert.Equal(nameof(OverrideComp), described.ComponentName);
        Assert.Equal("A", described.MemberName);
        Assert.Equal("Child > OverrideComp", described.Group);
        Assert.True(described.Resolvable);

        // What the prefab says beside what the instance says, which is the question being asked.
        Assert.Equal("2", described.SourceValue);
        Assert.Equal("42", described.InstanceValue);
    }

    [Fact]
    public void DescribeOverrides_MarksAnEntryThatAddressesNothing()
    {
        Guid g = MakeNestedPrefab("DescribeBroken.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.PrefabOverrides.Add(new PropertyOverride
        {
            Path = $"{Guid.NewGuid()}/{Guid.NewGuid()}/A",
            Value = Serializer.Serialize(typeof(int), 1)
        });

        var described = Assert.Single(PrefabUtility.DescribeOverrides(instance));
        Assert.False(described.Resolvable);
    }

    [Fact]
    public void DescribeOverrides_GroupsAGameObjectLevelOverrideUnderTheObject()
    {
        Guid g = MakeNestedPrefab("DescribeGo.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.Children[0].Enabled = false;
        PrefabUtility.ReconcileInstance(instance);

        var described = Assert.Single(PrefabUtility.DescribeOverrides(instance));
        Assert.Equal("Child", described.Group);   // no component, so the object is the whole heading
        Assert.Equal("Enabled", described.MemberName);
    }

    [Fact]
    public void OverrideDetection_DoesNotTakeAClassesWordForIt()
    {
        var root = new GameObject("Root");
        root.AddComponent<LooseComp>().Held = new LooseEquality { Value = 1 };
        Guid g = CreatePrefabAsset(root, "Loose.prefab");

        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.GetComponent<LooseComp>()!.Held = new LooseEquality { Value = 2 };
        PrefabUtility.ReconcileInstance(instance);

        // Comparison starts by asking cheap questions, and a class is free to answer that two
        // different states are equal. Only things whose equality is about their value get asked.
        Assert.Single(instance.PrefabOverrides);

        PrefabUtility.RefreshAllInstances(g);
        Assert.Equal(2, Scene.Current!.RootObjects.First().GetComponent<LooseComp>()!.Held.Value);
    }

    // ---------------------------------------------------------------------
    // What the instance has that the prefab does not
    // ---------------------------------------------------------------------

    [Fact]
    public void DescribeAdditions_FindsAddedComponentsAndObjects()
    {
        Guid g = MakeNestedPrefab("Additions.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.Children[0].AddComponent<VecComp>();
        var added = new GameObject("Extra");
        added.SetParent(instance);

        var describedAdditions = PrefabUtility.DescribeAdditions(instance);
        Assert.Equal(2, describedAdditions.Count);

        var component = describedAdditions.Single(a => !a.IsWholeObject);
        Assert.Equal("Child > VecComp", component.Label);

        var whole = describedAdditions.Single(a => a.IsWholeObject);
        Assert.Equal("Extra", whole.Label);

        // What the prefab provides is not an addition, however much of it there is.
        Assert.DoesNotContain(describedAdditions, a => a.ObjectName == "Root" && a.ComponentName == "OverrideComp");
    }

    [Fact]
    public void RemoveAddition_TakesAnAddedComponentBackOut()
    {
        Guid g = MakeNestedPrefab("AddRemove.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.AddComponent<VecComp>();
        var addition = PrefabUtility.DescribeAdditions(instance).Single(a => !a.IsWholeObject);

        PrefabUtility.RemoveAddition(instance, addition);

        Assert.Null(instance.GetComponent<VecComp>());
        Assert.Empty(PrefabUtility.DescribeAdditions(instance));
    }

    [Fact]
    public void ApplyAddition_PutsAnAddedComponentIntoThePrefab()
    {
        Guid g = MakeNestedPrefab("AddApplyComp.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.AddComponent<VecComp>().V = new Float3(1, 2, 3);
        var addition = PrefabUtility.DescribeAdditions(instance).Single(a => !a.IsWholeObject);

        PrefabUtility.ApplyAddition(instance, addition);

        // In the prefab now, with the values it was given.
        var fresh = Inst(g);
        Assert.NotNull(fresh.GetComponent<VecComp>());
        Assert.Equal(1.0, fresh.GetComponent<VecComp>()!.V.X, 3);

        // And the instance stops carrying it as its own, so a refresh does not hand it a second copy.
        var live = Scene.Current!.RootObjects.First();
        Assert.Empty(PrefabUtility.DescribeAdditions(live));
        Assert.Single(live.GetComponents<VecComp>());

        PrefabUtility.RefreshAllInstances(g);
        Assert.Single(Scene.Current!.RootObjects.First().GetComponents<VecComp>());
    }

    [Fact]
    public void ApplyAddition_PutsAnAddedObjectIntoThePrefab()
    {
        Guid g = MakeNestedPrefab("AddApplyObj.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        var added = new GameObject("Extra");
        added.AddComponent<VecComp>().V = new Float3(4, 5, 6);
        added.SetParent(instance);

        var addition = PrefabUtility.DescribeAdditions(instance).Single(a => a.IsWholeObject);
        PrefabUtility.ApplyAddition(instance, addition);

        var fresh = Inst(g);
        var freshChild = fresh.Children.Single(c => c.Name == "Extra");
        Assert.Equal(4.0, freshChild.GetComponent<VecComp>()!.V.X, 3);

        var live = Scene.Current!.RootObjects.First();
        Assert.Empty(PrefabUtility.DescribeAdditions(live));

        PrefabUtility.RefreshAllInstances(g);
        Assert.Single(Scene.Current!.RootObjects.First().Children.Where(c => c.Name == "Extra"));
    }

    [Fact]
    public void ApplyAddition_WithAnotherAdditionBesideIt_AppliesTheRightOne()
    {
        Guid g = MakeNestedPrefab("AddApplyTwo.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        // Two added objects, and only the second one applied. The prefab then has one child the
        // instance has three of, so nothing lines up by position any more.
        var first = new GameObject("FirstAdded");
        first.SetParent(instance);
        var second = new GameObject("SecondAdded");
        second.AddComponent<VecComp>().V = new Float3(7, 0, 0);
        second.SetParent(instance);

        var addition = PrefabUtility.DescribeAdditions(instance).Single(a => a.ObjectName == "SecondAdded");
        PrefabUtility.ApplyAddition(instance, addition);
        PrefabUtility.RefreshAllInstances(g);

        var live = Scene.Current!.RootObjects.First();
        Assert.Equal(7.0, live.Children.Single(c => c.Name == "SecondAdded").GetComponent<VecComp>()!.V.X, 3);

        // The one that was not applied is untouched and still the instance's own.
        Assert.NotNull(live.Children.SingleOrDefault(c => c.Name == "FirstAdded"));
        Assert.Equal("FirstAdded", Assert.Single(PrefabUtility.DescribeAdditions(live)).ObjectName);

        // And the prefab took only what it was given.
        var fresh = Inst(g);
        Assert.DoesNotContain(fresh.Children, c => c.Name == "FirstAdded");
        Assert.Contains(fresh.Children, c => c.Name == "SecondAdded");
    }

    [Fact]
    public void ApplyAddition_LeavesOtherInstancesToTheRefresh()
    {
        Guid g = MakeNestedPrefab("AddApplyMany.prefab");
        var a = Inst(g);
        var b = Inst(g);
        LoadSceneWith(a, b);

        a.AddComponent<VecComp>();
        PrefabUtility.ApplyAddition(a, PrefabUtility.DescribeAdditions(a).Single());

        // The prefab has it now, so every instance does, and none of them call it their own.
        Assert.NotNull(b.GetComponent<VecComp>());
        Assert.Empty(PrefabUtility.DescribeAdditions(b));
    }

    // ---------------------------------------------------------------------
    // Acting on one component rather than the whole instance
    // ---------------------------------------------------------------------

    [Fact]
    public void RevertComponentOverrides_TakesBackThatComponentAndLeavesTheRest()
    {
        Guid g = MakeNestedPrefab("RevertComp.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        var rootComp = instance.GetComponent<OverrideComp>()!;
        rootComp.A = 42;
        rootComp.B = 43;
        instance.Children[0].GetComponent<OverrideComp>()!.A = 99;
        PrefabUtility.ReconcileInstance(instance);
        Assert.Equal(3, instance.PrefabOverrides.Count);

        PrefabUtility.RevertComponentOverrides(instance, rootComp);

        Assert.Equal(1, rootComp.A);
        Assert.Equal(1, rootComp.B);
        Assert.Equal(99, instance.Children[0].GetComponent<OverrideComp>()!.A);   // not its business
        Assert.Single(instance.PrefabOverrides);
    }

    [Fact]
    public void RevertComponentOverrides_IsOneUndoStep()
    {
        Guid g = MakeNestedPrefab("RevertCompUndo.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 42;
        comp.B = 43;
        PrefabUtility.ReconcileInstance(instance);

        Undo.Clear();
        PrefabUtility.RevertComponentOverrides(instance, comp);
        Undo.PerformUndo();

        var live = Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!;
        Assert.Equal(42, live.A);
        Assert.Equal(43, live.B);
        Assert.Equal(2, Scene.Current!.RootObjects.First().PrefabOverrides.Count);
        Assert.False(Undo.CanUndo);   // two members reverted, one step
    }

    [Fact]
    public void ApplyComponentOverrides_PushesOnlyThatComponentToThePrefab()
    {
        Guid g = MakeNestedPrefab("ApplyComp.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        var rootComp = instance.GetComponent<OverrideComp>()!;
        rootComp.A = 42;
        instance.Children[0].GetComponent<OverrideComp>()!.A = 99;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.ApplyComponentOverrides(instance, rootComp);

        var fresh = Inst(g);
        Assert.Equal(42, fresh.GetComponent<OverrideComp>()!.A);
        Assert.Equal(2, fresh.Children[0].GetComponent<OverrideComp>()!.A);   // still the prefab's
    }

    [Fact]
    public void ResetComponentToDefaults_PutsBackWhatANewOneWouldHold()
    {
        var go = new GameObject("Plain");
        var comp = go.AddComponent<OverrideComp>();
        comp.A = 42;
        comp.B = 43;
        LoadSceneWith(go);

        Undo.Clear();
        PrefabUtility.ResetComponentToDefaults(go, comp);

        Assert.Equal(0, comp.A);
        Assert.Equal(0, comp.B);

        Undo.PerformUndo();
        Assert.Equal(42, comp.A);
        Assert.Equal(43, comp.B);
    }

    [Fact]
    public void ResetComponentToDefaults_KeepsTheComponentsIdentity()
    {
        var go = new GameObject("Plain");
        var comp = go.AddComponent<OverrideComp>();
        comp.A = 42;
        LoadSceneWith(go);

        Guid identity = comp.Identifier;
        PrefabUtility.ResetComponentToDefaults(go, comp);

        // The same component with different values, not a new one; anything addressing it by
        // identifier, an undo record above all, still finds it.
        Assert.Equal(identity, comp.Identifier);
        Assert.Same(comp, go.GetComponentByIdentifier(identity));
    }

    // ---------------------------------------------------------------------
    // Odds and ends
    // ---------------------------------------------------------------------

    [Fact]
    public void SetPropertyModifications_RebuildsOnlyTheInstanceItWasHanded()
    {
        Guid g = MakeNestedPrefab("SetOnly.prefab");
        var a = Inst(g);
        var b = Inst(g);
        LoadSceneWith(a, b);

        var bComp = b.GetComponent<OverrideComp>()!;
        bComp.A = 5;
        PrefabUtility.RecordComponentOverrides(b, bComp);

        var rebuilt = new List<GameObject>();
        void OnUpdated(GameObject go) => rebuilt.Add(go);
        PrefabUtility.OnPrefabInstanceUpdated += OnUpdated;
        try
        {
            PrefabUtility.SetPropertyModifications(a, [
                new PropertyOverride
                {
                    Path = PrefabUtility.GetOverridePath(a, a.GetComponent<OverrideComp>()!, "A"),
                    Value = Serializer.Serialize(typeof(int), 42)
                }
            ]);
        }
        finally { PrefabUtility.OnPrefabInstanceUpdated -= OnUpdated; }

        Assert.Equal(42, a.GetComponent<OverrideComp>()!.A);
        Assert.Equal(5, b.GetComponent<OverrideComp>()!.A);

        // The others are instances of the same prefab, not of this one. Rebuilding them lands on the
        // same values it started from, so the cost and the churn are all it amounts to.
        Assert.Equal([a], rebuilt);
    }

    [Fact]
    public void SetPropertyModifications_IsUndoable()
    {
        Guid g = MakeNestedPrefab("SetUndo.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        Undo.Clear();
        PrefabUtility.SetPropertyModifications(instance, [
            new PropertyOverride
            {
                Path = PrefabUtility.GetOverridePath(instance, instance.GetComponent<OverrideComp>()!, "A"),
                Value = Serializer.Serialize(typeof(int), 42)
            }
        ]);
        Assert.Equal(42, instance.GetComponent<OverrideComp>()!.A);

        Undo.PerformUndo();

        var live = Scene.Current!.RootObjects.First();
        Assert.Empty(live.PrefabOverrides);
        Assert.Equal(1, live.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ConnectGameObjectToPrefab_SaysWhatItCouldNotMatch()
    {
        Guid g = MakeNestedPrefab("Connect.prefab");

        // Same shape as the prefab plus a component it has no counterpart for.
        var stray = new GameObject("Root");
        stray.AddComponent<OverrideComp>();
        stray.AddComponent<VecComp>();
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>();
        child.SetParent(stray);
        LoadSceneWith(stray);

        var warnings = CaptureWarnings(() =>
            Assert.True(PrefabUtility.ConnectGameObjectToPrefab(stray, g)));

        Assert.Contains(warnings, w => w.Contains("VecComp"));
    }

    [Fact]
    public void PrefabEditingMode_SavingRaisesSavedAndWritesNoNestedLinks()
    {
        Guid inner = AuthorLeaf("Save_Inner", 7);
        Guid outer = AuthorLeaf("Save_Outer", 1);

        var sceneObject = new GameObject("InTheScene");
        LoadSceneWith(sceneObject);

        var saved = new List<Guid>();
        void OnSaved(Guid g) => saved.Add(g);
        PrefabUtility.OnPrefabSaved += OnSaved;
        try
        {
            PrefabEditingMode.Enter(outer);
            Scene.ProcessPendingLoad();
            Assert.True(PrefabEditingMode.IsEditing);

            // Something the session adds that answers to another prefab.
            var editingRoot = Scene.Current!.RootObjects.First(go => !go.HideFlags.HasFlag(HideFlags.HideAndDontSave));
            Inst(inner).SetParent(editingRoot);

            Assert.True(PrefabEditingMode.Save());
        }
        finally
        {
            PrefabUtility.OnPrefabSaved -= OnSaved;
            PrefabEditingMode.Exit();
            Scene.ProcessPendingLoad();
        }

        // Listeners hear about a prefab-mode save the same as any other write.
        Assert.Contains(outer, saved);

        // The importer drops nested links on the way in, so a file that still carried them would
        // describe something the asset built from it is not.
        Assert.DoesNotContain(inner.ToString(), File.ReadAllText(AssetAbsolutePath("Save_Outer.prefab")));
    }

    [Fact]
    public void PrefabEditingMode_KeepsTheScenesUndoHistory()
    {
        Guid g = AuthorLeaf("Undo_Prefab", 1);

        var sceneObject = new GameObject("InTheScene");
        LoadSceneWith(sceneObject);

        Undo.Clear();
        int value = 0;
        Undo.RegisterAction("Scene Edit", () => value = 1, () => value = 2);
        Undo.IncrementGroup();

        PrefabEditingMode.Enter(g);
        Scene.ProcessPendingLoad();
        Assert.False(Undo.CanUndo); // the session starts with nothing behind it

        PrefabEditingMode.Exit();
        Scene.ProcessPendingLoad();

        // Glancing at a prefab used to cost every step taken in the scene.
        Assert.True(Undo.CanUndo);
        Undo.PerformUndo();
        Assert.Equal(1, value);
    }

    // ---------------------------------------------------------------------
    // Redo replays an operation without recording it a second time
    // ---------------------------------------------------------------------

    [Fact]
    public void RedoingASingleRevert_DoesNotPushAnotherUndoStep()
    {
        Guid g = MakeNestedPrefab("RedoRevert.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 42;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        string path = PrefabUtility.GetOverridePath(instance, comp, "A");

        Undo.Clear();
        PrefabUtility.RevertSingleOverride(instance, path);

        Undo.PerformUndo();
        Undo.PerformRedo();

        // Stepping back over the redo has to leave the history empty. A redo that registers as it
        // replays leaves a second copy of itself behind, so undo keeps having something to do.
        Undo.PerformUndo();
        Assert.Equal(42, Scene.Current!.RootObjects.First().GetComponent<OverrideComp>()!.A);
        Assert.False(Undo.CanUndo);
    }

    // ---------------------------------------------------------------------
    // Source identities are read off the objects, not paired up by position
    // ---------------------------------------------------------------------

    /// <summary>Collects warnings and errors raised while <paramref name="act"/> runs.</summary>
    private static List<string> CaptureWarnings(Action act)
    {
        var logs = new List<string>();
        void Capture(string m, DebugStackTrace? _, LogSeverity s)
        {
            if (s is LogSeverity.Error or LogSeverity.Warning)
                lock (logs) logs.Add(m);
        }

        Debug.OnLog += Capture;
        try { act(); }
        finally { Debug.OnLog -= Capture; }
        return logs;
    }

    [Fact]
    public void AComponentThatFailsToLoad_DoesNotShiftTheIdentitiesOfTheOnesAfterIt()
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = 7;
        root.AddComponent<VecComp>().V = new Float3(1, 2, 3);

        // A component entry nothing can be made of, ahead of the real ones. Deserialization drops it
        // outright rather than leaving a placeholder, so the live list is one shorter than the data.
        EchoObject echo = Serializer.Serialize(typeof(object), root);
        echo.Get("Components")!.List.Insert(0, EchoObject.NewCompound());
        File.WriteAllText(AssetAbsolutePath("Shift.prefab"), echo.WriteToString());
        Guid g = Assets.ImportFile("Shift.prefab");

        var instance = GameObject.InstantiateDetached(GetPrefab(g)!)!;
        Assert.Equal(2, instance.GetComponents<MonoBehaviour>().Count());

        // Each component's source identity is its own, not the one belonging to the entry before it.
        var written = EchoObject.ReadFromString(File.ReadAllText(AssetAbsolutePath("Shift.prefab")))
            .Get("Components")!.List;

        foreach (MonoBehaviour live in instance.GetComponents<MonoBehaviour>())
        {
            EchoObject entry = written.First(e => e.Get("$type")?.StringValue?.Contains(live.GetType().Name) == true);
            Assert.Equal(Guid.Parse(entry.Get("_identifier")!.StringValue),
                instance.GetComponentSourceIdentifier(live));
        }
    }

    [Fact]
    public void InstantiatingTwice_GivesEachInstanceItsOwnIdentifiers()
    {
        Guid g = MakeNestedPrefab("Ident.prefab");

        var a = Inst(g);
        var b = Inst(g);

        // Preserving identifiers through the load is an internal step, not something the caller sees.
        Assert.NotEqual(a.Identifier, b.Identifier);
        Assert.NotEqual(a.Children[0].Identifier, b.Children[0].Identifier);
        Assert.NotEqual(a.GetComponent<OverrideComp>()!.Identifier, b.GetComponent<OverrideComp>()!.Identifier);

        // While the source identities, which say where each came from, are shared.
        Assert.Equal(a.SourceIdentifier, b.SourceIdentifier);
        Assert.Equal(a.Children[0].SourceIdentifier, b.Children[0].SourceIdentifier);
    }

    // ---------------------------------------------------------------------
    // Writing a prefab
    // ---------------------------------------------------------------------

    [Fact]
    public void ApplyingLeavesNoTemporaryFileBehind()
    {
        Guid g = MakeNestedPrefab("Atomic.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);
        PrefabUtility.ApplyOverrides(instance);

        Assert.Empty(Directory.GetFiles(Project.AssetsPath, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal(42, Inst(g).GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ApplyingAReferenceToASceneObject_SaysItWasDropped()
    {
        var root = new GameObject("Root");
        root.AddComponent<RefHolderComp>();
        Guid g = CreatePrefabAsset(root, "Dropped.prefab");

        var instance = Inst(g);
        var target = new GameObject("SceneTarget");
        SetSceneCurrent(instance, target);

        var comp = instance.GetComponent<RefHolderComp>()!;
        comp.Target = target;
        PrefabUtility.RecordComponentOverrides(instance, comp);

        var warnings = CaptureWarnings(() =>
            PrefabUtility.ApplySingleOverride(instance, PrefabUtility.GetOverridePath(instance, comp, "Target")));

        // The reference cannot go into the asset, and the author has to be told rather than finding
        // an empty field in every instance later.
        Assert.Contains(warnings, w => w.Contains("SceneTarget"));
    }

    // ---------------------------------------------------------------------
    // What the prefab provides is not the instance's to take away
    // ---------------------------------------------------------------------

    [Fact]
    public void AComponentThePrefabProvides_CannotBeRemovedFromAnInstance()
    {
        Guid g = MakeNestedPrefab("Locked.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.RemoveComponent(instance.GetComponent<OverrideComp>()!);

        // Refused rather than undone later: nothing records that it went, so a refresh would put it
        // back and the edit would read as having silently failed some time after the fact.
        Assert.NotNull(instance.GetComponent<OverrideComp>());
    }

    [Fact]
    public void AComponentTheInstanceAdded_CanBeRemovedFromIt()
    {
        Guid g = MakeNestedPrefab("LockedAdd.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.AddComponent<VecComp>();
        instance.RemoveComponent(instance.GetComponent<VecComp>()!);

        Assert.Null(instance.GetComponent<VecComp>());
    }

    [Fact]
    public void UnpackingAnInstance_LetsItsComponentsBeRemoved()
    {
        Guid g = MakeNestedPrefab("LockedUnpack.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        PrefabUtility.UnpackPrefabInstance(instance);
        instance.RemoveComponent(instance.GetComponent<OverrideComp>()!);

        Assert.Null(instance.GetComponent<OverrideComp>());
    }

    [Fact]
    public void ThePrefabDroppingAComponent_StillRemovesItFromInstances()
    {
        Guid g = MakeNestedPrefab("LockedDrop.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        // The guard is about what the instance may do, not about what the prefab may do to it.
        EditPrefabSource(g, "LockedDrop.prefab", s => s.RemoveComponent(s.GetComponent<OverrideComp>()!));

        Assert.Null(Scene.Current!.RootObjects.First().GetComponent<OverrideComp>());
    }

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

        PrefabUtility.RecordComponentOverrides(childGo, comp);

        Assert.Single(instance.PrefabOverrides);   // stored on the root...
        Assert.Empty(childGo.PrefabOverrides);      // ...not on the child
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(childGo, comp, "A")));
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
        PrefabUtility.RecordComponentOverrides(instance.Children[0], comp);

        PrefabUtility.RevertSingleOverride(instance, PrefabUtility.GetOverridePath(instance.Children[0], comp, "A"));

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
        PrefabUtility.RecordComponentOverrides(instance, vec); // records an override on the VecComp
        SetSceneCurrent(instance);

        // Source structure changes: the VecComp is removed, so the override has nothing to land on.
        EditPrefabSource(g, "Stale.prefab", src => src.RemoveComponent(src.GetComponent<VecComp>()!));

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

        PrefabUtility.RecordComponentOverrides(instance, original);

        // Reordering used to shift every component's index and stop detection working at all. Paths
        // name the source component, so the override lands on the right one regardless.
        Assert.Single(instance.PrefabOverrides);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, PrefabUtility.GetOverridePath(instance, original, "A")));
        Assert.Equal(42, original.A);
    }

    #endregion

    #region Reconcile

    private Scene LoadSceneWith(params GameObject[] objects)
    {
        var scene = new Scene();
        foreach (var o in objects) scene.Add(o);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        return Scene.Current!;
    }

    /// <summary>A prefab of Root(OverrideComp A=1,B=1) with a Child(OverrideComp A=2).</summary>
    private Guid MakeNestedPrefab(string path)
    {
        var root = new GameObject("Root");
        var rootComp = root.AddComponent<OverrideComp>();
        rootComp.A = 1; rootComp.B = 1;

        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 2;
        child.SetParent(root);

        return CreatePrefabAsset(root, path);
    }

    // ---------------------------------------------------------------------
    // Identity: the instance is updated, not replaced
    // ---------------------------------------------------------------------

    [Fact]
    public void TheInstanceObjectsAreTheSameObjectsAfterwards()
    {
        Guid g = MakeNestedPrefab("Id.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        var child = instance.Children[0];
        var component = instance.GetComponent<OverrideComp>()!;
        Guid instanceId = instance.Identifier;
        Guid componentId = component.Identifier;

        PrefabUtility.RefreshAllInstances(g);

        Assert.Same(instance, Scene.Current!.RootObjects.First());
        Assert.Same(child, instance.Children[0]);
        Assert.Same(component, instance.GetComponent<OverrideComp>());
        Assert.False(instance.IsDisposed);
        Assert.False(child.IsDisposed);
        Assert.False(component.IsDisposed);

        // Identifiers are what undo records and scene references resolve through.
        Assert.Equal(instanceId, instance.Identifier);
        Assert.Equal(componentId, instance.GetComponent<OverrideComp>()!.Identifier);
    }

    [Fact]
    public void ReferencesIntoTheInstanceStillPointAtIt()
    {
        Guid g = MakeNestedPrefab("Refs.prefab");
        var instance = Inst(g);
        var holder = new GameObject("Holder");
        var link = holder.AddComponent<LinkComp>();
        LoadSceneWith(instance, holder);

        link.Target = instance.Children[0];
        link.Component = instance.GetComponent<OverrideComp>();

        PrefabUtility.RefreshAllInstances(g);

        Assert.Same(instance.Children[0], link.Target);
        Assert.Same(instance.GetComponent<OverrideComp>(), link.Component);
        Assert.False(link.Target!.IsDisposed);
        Assert.False(link.Component!.IsDisposed);
    }

    [Fact]
    public void TheSelectionIsUntouched()
    {
        Guid g = MakeNestedPrefab("Sel.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        Selection.Clear();
        Selection.AddToSelection(instance);
        Selection.AddToSelection(instance.Children[0]);

        PrefabUtility.RefreshAllInstances(g);

        var selected = Selection.GetSelected<GameObject>().ToList();
        Assert.Equal(2, selected.Count);
        Assert.Contains(instance, selected);
        Assert.Contains(instance.Children[0], selected);
    }

    [Fact]
    public void ThePlaceInTheSceneIsKept()
    {
        Guid g = MakeNestedPrefab("Place.prefab");
        var parent = new GameObject("Parent");
        var before = new GameObject("Before");
        var instance = Inst(g);
        var after = new GameObject("After");
        LoadSceneWith(parent, before, after);

        instance.Scene?.Add(instance);
        Scene.Current!.Add(instance);
        instance.SetParent(parent);
        instance.SetSiblingIndex(0);

        PrefabUtility.RefreshAllInstances(g);

        Assert.Same(parent, instance.Parent);
        Assert.Equal(0, instance.GetSiblingIndex());
    }

    // ---------------------------------------------------------------------
    // What the prefab decides
    // ---------------------------------------------------------------------

    [Fact]
    public void ChangedValuesFollowThePrefab()
    {
        Guid g = MakeNestedPrefab("Values.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        EditPrefabSource(g, "Values.prefab", src =>
        {
            src.GetComponent<OverrideComp>()!.B = 9;
            src.Children[0].GetComponent<OverrideComp>()!.A = 8;
        });
        PrefabUtility.RefreshAllInstances(g);

        Assert.Equal(9, instance.GetComponent<OverrideComp>()!.B);
        Assert.Equal(8, instance.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void AComponentAddedToThePrefabAppears()
    {
        Guid g = MakeNestedPrefab("AddComp.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        Assert.Null(instance.GetComponent<VecComp>());

        EditPrefabSource(g, "AddComp.prefab", src => src.AddComponent<VecComp>().V = new Float3(1, 2, 3));
        PrefabUtility.RefreshAllInstances(g);

        var added = instance.GetComponent<VecComp>();
        Assert.NotNull(added);
        Assert.Equal(1.0, added!.V.X, 3);
        // It belongs to the prefab, so it is tracked as such rather than looking instance-added.
        Assert.NotEqual(Guid.Empty, instance.GetComponentSourceIdentifier(added));
    }

    [Fact]
    public void AComponentRemovedFromThePrefabGoes()
    {
        Guid g = MakeNestedPrefab("DelComp.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        EditPrefabSource(g, "DelComp.prefab", src => src.RemoveComponent(src.GetComponent<OverrideComp>()!));
        PrefabUtility.RefreshAllInstances(g);

        Assert.Null(instance.GetComponent<OverrideComp>());
        // The child's own component is a different one and is untouched.
        Assert.NotNull(instance.Children[0].GetComponent<OverrideComp>());
    }

    [Fact]
    public void AChildAddedToThePrefabAppears()
    {
        Guid g = MakeNestedPrefab("AddChild.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        EditPrefabSource(g, "AddChild.prefab", src =>
        {
            var extra = new GameObject("Extra");
            extra.AddComponent<VecComp>().V = new Float3(4, 0, 0);
            extra.SetParent(src);
        });
        PrefabUtility.RefreshAllInstances(g);

        var extra = instance.Children.FirstOrDefault(c => c.Name == "Extra");
        Assert.NotNull(extra);
        Assert.Equal(4.0, extra!.GetComponent<VecComp>()!.V.X, 3);
        Assert.Same(instance, extra.Parent);
        Assert.Same(Scene.Current, extra.Scene);
        // Tracked as prefab content, so a later prefab change can reach it.
        Assert.NotEqual(Guid.Empty, extra.SourceIdentifier);
    }

    [Fact]
    public void AChildRemovedFromThePrefabGoes()
    {
        Guid g = MakeNestedPrefab("DelChild.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        // The source tree is detached, so unparenting is what takes the child out of the prefab.
        EditPrefabSource(g, "DelChild.prefab", src => src.Children[0].SetParent(null!));
        PrefabUtility.RefreshAllInstances(g);

        Assert.Empty(instance.Children);
    }

    [Fact]
    public void ChildOrderFollowsThePrefab()
    {
        var root = new GameObject("Root");
        foreach (string name in new[] { "A", "B", "C" })
            new GameObject(name).SetParent(root);
        Guid g = CreatePrefabAsset(root, "Order.prefab");

        var instance = Inst(g);
        LoadSceneWith(instance);
        Assert.Equal("A,B,C", string.Join(",", instance.Children.Select(c => c.Name)));

        EditPrefabSource(g, "Order.prefab", src => src.Children.First(c => c.Name == "C").SetSiblingIndex(0));
        PrefabUtility.RefreshAllInstances(g);

        Assert.Equal("C,A,B", string.Join(",", instance.Children.Select(c => c.Name)));
    }

    [Fact]
    public void DeepDescendantsAreReached()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        var grand = new GameObject("Grand");
        grand.AddComponent<OverrideComp>().A = 1;
        child.SetParent(root); grand.SetParent(child);
        Guid g = CreatePrefabAsset(root, "Deep.prefab");

        var instance = Inst(g);
        LoadSceneWith(instance);
        var grandInstance = instance.Children[0].Children[0];

        EditPrefabSource(g, "Deep.prefab", src => src.Children[0].Children[0].GetComponent<OverrideComp>()!.A = 12);
        PrefabUtility.RefreshAllInstances(g);

        Assert.Same(grandInstance, instance.Children[0].Children[0]);
        Assert.Equal(12, grandInstance.GetComponent<OverrideComp>()!.A);
    }

    // ---------------------------------------------------------------------
    // What the instance keeps
    // ---------------------------------------------------------------------

    [Fact]
    public void OverriddenValuesAreKeptWhileTherestFollowsThePrefab()
    {
        Guid g = MakeNestedPrefab("Ovr.prefab");
        var instance = Inst(g);
        var comp = instance.GetComponent<OverrideComp>()!;
        comp.A = 99;
        PrefabUtility.RecordComponentOverrides(instance, comp);
        LoadSceneWith(instance);

        EditPrefabSource(g, "Ovr.prefab", src =>
        {
            var c = src.GetComponent<OverrideComp>()!;
            c.A = 5;  // overridden on the instance, so it must not win
            c.B = 7;  // not overridden, so it must
        });
        PrefabUtility.RefreshAllInstances(g);

        Assert.Equal(99, comp.A);
        Assert.Equal(7, comp.B);
    }

    [Fact]
    public void ComponentsAndChildrenTheInstanceAddedAreLeftAlone()
    {
        Guid g = MakeNestedPrefab("Added.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        var addedComponent = instance.AddComponent<VecComp>();
        addedComponent.V = new Float3(5, 5, 5);

        var addedChild = new GameObject("InstanceChild");
        Scene.Current!.Add(addedChild);
        addedChild.SetParent(instance);

        EditPrefabSource(g, "Added.prefab", src => src.GetComponent<OverrideComp>()!.B = 3);
        PrefabUtility.RefreshAllInstances(g);

        Assert.Same(addedComponent, instance.GetComponent<VecComp>());
        Assert.Equal(5.0, addedComponent.V.X, 3);
        Assert.Contains(instance.Children, c => ReferenceEquals(c, addedChild));
        Assert.False(addedChild.IsDisposed);
        Assert.Equal(3, instance.GetComponent<OverrideComp>()!.B); // and the prefab change still landed
    }

    [Fact]
    public void PerInstanceStateIsKept()
    {
        Guid g = MakeNestedPrefab("PerInst.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        instance.Name = "Placed";
        instance.Transform.Position = new Float3(4, 5, 6);
        instance.Transform.LocalScale = new Float3(2, 2, 2);
        instance.HideFlags = HideFlags.NoGizmos;

        EditPrefabSource(g, "PerInst.prefab", src => src.GetComponent<OverrideComp>()!.B = 4);
        PrefabUtility.RefreshAllInstances(g);

        Assert.Equal("Placed", instance.Name);
        Assert.Equal(4.0, instance.Transform.Position.X, 3);
        Assert.Equal(2.0, instance.Transform.LocalScale.X, 3);
        Assert.Equal(HideFlags.NoGizmos, instance.HideFlags);
    }

    [Fact]
    public void SeveralInstancesKeepTheirOwnOverrides()
    {
        Guid g = MakeNestedPrefab("Many.prefab");
        var a = Inst(g);
        var b = Inst(g);
        LoadSceneWith(a, b);

        var ca = a.GetComponent<OverrideComp>()!;
        var cb = b.GetComponent<OverrideComp>()!;
        ca.A = 10; PrefabUtility.RecordComponentOverrides(a, ca);
        cb.A = 20; PrefabUtility.RecordComponentOverrides(b, cb);

        EditPrefabSource(g, "Many.prefab", src => src.GetComponent<OverrideComp>()!.B = 6);
        PrefabUtility.RefreshAllInstances(g);

        Assert.Equal((10, 6), (ca.A, ca.B));
        Assert.Equal((20, 6), (cb.A, cb.B));
    }

    // ---------------------------------------------------------------------
    // Content that came from a flattened prefab
    // ---------------------------------------------------------------------

    [Fact]
    public void AnOverrideOnFlattenedContentSurvivesARefresh()
    {
        var innerSource = new GameObject("Inner");
        innerSource.AddComponent<OverrideComp>().A = 1;
        Guid inner = CreatePrefabAsset(innerSource, "Inner.prefab");

        var outerSource = new GameObject("Outer");
        Inst(inner).SetParent(outerSource);
        File.WriteAllText(AssetAbsolutePath("Outer.prefab"),
            Serializer.Serialize(typeof(object), outerSource).WriteToString());
        Guid outer = Assets.ImportFile("Outer.prefab");

        var instance = Inst(outer);
        LoadSceneWith(instance);

        // Prefabs do not nest, so the once-nested object is ordinary content of the outer prefab and
        // an edit to it is an ordinary override on this instance.
        var child = instance.Children[0];
        var childComp = child.GetComponent<OverrideComp>()!;
        childComp.A = 77;
        PrefabUtility.RecordComponentOverrides(child, childComp);

        PrefabUtility.RefreshAllInstances(outer);

        Assert.Same(child, instance.Children[0]);
        Assert.Equal(77, instance.Children[0].GetComponent<OverrideComp>()!.A);
    }

    // ---------------------------------------------------------------------
    // Reimport drives the same path
    // ---------------------------------------------------------------------

    [Fact]
    public void ReimportUpdatesInstancesWithoutReplacingThem()
    {
        Guid g = MakeNestedPrefab("Reimport.prefab");
        var instance = Inst(g);
        LoadSceneWith(instance);

        var comp = instance.GetComponent<OverrideComp>()!;

        // No explicit refresh call: the import notification drives it.
        EditPrefabSource(g, "Reimport.prefab", src => src.GetComponent<OverrideComp>()!.B = 11);

        Assert.Same(instance, Scene.Current!.RootObjects.First());
        Assert.Same(comp, instance.GetComponent<OverrideComp>());
        Assert.Equal(11, comp.B);
    }

    #region References inside the prefab

    [Fact]
    public void ReferencesInsideThePrefabPointAtTheInstancesOwnObjects()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 3;
        child.SetParent(root);

        var link = root.AddComponent<LinkComp>();
        link.Target = child;
        link.Component = child.GetComponent<OverrideComp>();

        Guid guid = CreatePrefabAsset(root, "Intra.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        LinkComp instanceLink = instance.GetComponent<LinkComp>()!;
        Assert.Same(instance.Children[0], instanceLink.Target);

        PrefabUtility.RefreshAllInstances(guid);

        Assert.NotNull(instanceLink.Target);
        Assert.Same(instance.Children[0], instanceLink.Target);
        Assert.Same(instance.Children[0].GetComponent<OverrideComp>(), instanceLink.Component);
    }

    [Fact]
    public void ReferencesInsideThePrefabAreIndependentPerInstance()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.SetParent(root);
        root.AddComponent<LinkComp>().Target = child;

        Guid guid = CreatePrefabAsset(root, "IntraTwo.prefab");
        GameObject first = Inst(guid);
        GameObject second = Inst(guid);
        LoadSceneWith(first, second);

        PrefabUtility.RefreshAllInstances(guid);

        Assert.Same(first.Children[0], first.GetComponent<LinkComp>()!.Target);
        Assert.Same(second.Children[0], second.GetComponent<LinkComp>()!.Target);
        Assert.NotSame(first.GetComponent<LinkComp>()!.Target, second.GetComponent<LinkComp>()!.Target);
    }

    [Fact]
    public void ReferencesInsideThePrefabSurviveASourceChange()
    {
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.AddComponent<OverrideComp>().A = 1;
        child.SetParent(root);
        root.AddComponent<LinkComp>().Target = child;

        Guid guid = CreatePrefabAsset(root, "IntraChange.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        EditPrefabSource(guid, "IntraChange.prefab",
            source => source.Children[0].GetComponent<OverrideComp>()!.A = 42);
        PrefabUtility.RefreshAllInstances(guid);

        Assert.Same(instance.Children[0], instance.GetComponent<LinkComp>()!.Target);
        Assert.Equal(42, instance.Children[0].GetComponent<OverrideComp>()!.A);
    }

    #endregion

    #endregion

    #region CloneIntegration

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
        Assert.Single(added.GetComponents<MonoBehaviour>().Where(c => c.SourceIdentifier != Guid.Empty));
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
        Assert.Empty(instance.GetComponents<MonoBehaviour>().Where(c => c.SourceIdentifier != Guid.Empty));
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
        int tracked = instance.GetComponents<MonoBehaviour>().Count(c => c.SourceIdentifier != Guid.Empty);

        PrefabUtility.RefreshAllInstances(guid);

        Assert.Equal(12, instance.GetComponent<OverrideComp>()!.A);
        Assert.Equal(tracked, instance.GetComponents<MonoBehaviour>().Count(c => c.SourceIdentifier != Guid.Empty));
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

    #endregion

    #region InstanceAddition

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

    #endregion

    #region Nesting

    private Guid Author(GameObject source, string relativePath)
    {
        // Only when it is not already in one. A test that builds its tree in the open scene first is
        // modelling the real order of events, and loading a scene around it here would swap the scene
        // out from under objects it is still holding.
        if (source.Scene == null) LoadSceneWith(source);
        Assert.True(PrefabUtility.SaveAsPrefabAssetAndConnect(source, relativePath));
        Assets.Refresh();

        var entry = EditorAssetBackend.Instance!.GetEntry(relativePath);
        Assert.NotNull(entry);
        return entry!.Guid;
    }

    private Guid AuthorLeaf(string name, int value)
    {
        var root = new GameObject(name);
        root.AddComponent<OverrideComp>().A = value;
        return Author(root, name + ".prefab");
    }

    /// <summary>A prefab authored from a root that contained an instance of <paramref name="nested"/>.</summary>
    private Guid AuthorContaining(string name, Guid nested)
    {
        var root = new GameObject(name);
        root.AddComponent<OverrideComp>().A = 0;
        Inst(nested).SetParent(root);
        return Author(root, name + ".prefab");
    }

    private static DependencyGraph Graph => EditorAssetBackend.Instance!.Dependencies;

    #region Creating and applying flattens

    [Fact]
    public void CreatingAPrefabBreaksTheLinkOfAnyInstanceInside()
    {
        Guid inner = AuthorLeaf("Flat_Inner", 7);
        Guid outer = AuthorContaining("Flat_Outer", inner);

        GameObject instance = Inst(outer);

        // The content is there, as content.
        Assert.Single(instance.Children);
        Assert.Equal(7, instance.Children[0].GetComponent<OverrideComp>()!.A);

        // But it is not an instance of the inner prefab.
        Assert.Equal(outer, instance.Children[0].PrefabAssetId);
        Assert.NotEqual(inner, instance.Children[0].PrefabAssetId);
    }

    [Fact]
    public void AFlattenedChildIsOrdinaryPrefabContent()
    {
        Guid inner = AuthorLeaf("Content_Inner", 7);
        Guid outer = AuthorContaining("Content_Outer", inner);

        GameObject instance = Inst(outer);
        GameObject child = instance.Children[0];

        // It has to carry a source identity, or overrides on it could never resolve.
        Assert.NotEqual(Guid.Empty, child.SourceIdentifier);
        Assert.True(PrefabUtility.IsProvidedByPrefab(child));
    }

    [Fact]
    public void EditingTheOnceNestedPrefabNoLongerReachesTheOuter()
    {
        Guid inner = AuthorLeaf("Detach_Inner", 7);
        Guid outer = AuthorContaining("Detach_Outer", inner);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        EditPrefabSource(inner, "Detach_Inner.prefab", src => src.GetComponent<OverrideComp>()!.A = 99);
        PrefabUtility.RefreshAllInstances(inner);

        // No link, so no propagation, and no divergence between this and a freshly spawned one either.
        Assert.Equal(7, instance.Children[0].GetComponent<OverrideComp>()!.A);
        Assert.Equal(7, Inst(outer).Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ApplyingNeverWritesAPrefabInstanceIntoTheAsset()
    {
        Guid inner = AuthorLeaf("ApplyFlat_Inner", 7);
        Guid outer = AuthorLeaf("ApplyFlat_Outer", 1);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        // Placed on the instance, so it is that instance's own content and stays there. The asset
        // cannot gain a nested prefab this way, or any other.
        GameObject placed = Inst(inner);
        placed.SetParent(instance);
        PrefabUtility.ApplyOverrides(instance);

        Assert.Empty(Inst(outer).Children);
        Assert.Contains(instance.Children, c => ReferenceEquals(c, placed));
    }

    [Fact]
    public void ADeeplyPlacedInstanceIsAlsoFlattened()
    {
        Guid inner = AuthorLeaf("DeepFlat_Inner", 7);

        var root = new GameObject("DeepFlat_Outer");
        var middle = new GameObject("Middle");
        middle.SetParent(root);
        Inst(inner).SetParent(middle);
        Guid outer = Author(root, "DeepFlat_Outer.prefab");

        GameObject instance = Inst(outer);
        Assert.NotEqual(inner, instance.Children[0].Children[0].PrefabAssetId);
    }

    [Fact]
    public void NothingRecordsADependencyOnAFlattenedPrefab()
    {
        Guid inner = AuthorLeaf("NoDep_Inner", 7);
        Guid outer = AuthorContaining("NoDep_Outer", inner);

        // The content was copied in, so there is nothing left to depend on.
        Assert.DoesNotContain(inner, Graph.GetDependencies(outer));
    }

    #endregion

    #region References across what would have been a boundary

    [Fact]
    public void AReferenceOutOfTheFlattenedContentStillResolves()
    {
        var innerRoot = new GameObject("Ref_Inner");
        innerRoot.AddComponent<LinkComp>();
        Guid inner = Author(innerRoot, "Ref_Inner.prefab");

        var outerRoot = new GameObject("Ref_Outer");
        LoadSceneWith(outerRoot);

        var marker = new GameObject("Marker");
        marker.SetParent(outerRoot);

        GameObject placed = Inst(inner);
        placed.SetParent(outerRoot);
        placed.GetComponent<LinkComp>()!.Target = marker;

        Guid outer = Author(outerRoot, "Ref_Outer.prefab");

        GameObject instance = Inst(outer);
        GameObject liveMarker = instance.Children.Single(c => c.Name == "Marker");
        LinkComp link = instance.Children.Single(c => c.Name != "Marker").GetComponent<LinkComp>()!;

        Assert.Same(liveMarker, link.Target);
    }

    [Fact]
    public void AReferenceIntoTheFlattenedContentStillResolves()
    {
        Guid inner = AuthorLeaf("In_Inner", 7);

        var outerRoot = new GameObject("In_Outer");
        var link = outerRoot.AddComponent<LinkComp>();
        GameObject placed = Inst(inner);
        placed.SetParent(outerRoot);
        link.Target = placed;

        Guid outer = Author(outerRoot, "In_Outer.prefab");

        GameObject instance = Inst(outer);
        Assert.Same(instance.Children[0], instance.GetComponent<LinkComp>()!.Target);
    }

    [Fact]
    public void ValuesChangedBeforeFlatteningAreKept()
    {
        Guid inner = AuthorLeaf("Val_Inner", 1);

        var outerRoot = new GameObject("Val_Outer");
        LoadSceneWith(outerRoot);

        GameObject placed = Inst(inner);
        placed.SetParent(outerRoot);
        placed.GetComponent<OverrideComp>()!.A = 55;

        Guid outer = Author(outerRoot, "Val_Outer.prefab");

        Assert.Equal(55, Inst(outer).Children[0].GetComponent<OverrideComp>()!.A);
    }

    #endregion

    #region Placing one instance inside another

    // Flattening is what writing an asset does, not what moving something in a scene does. An
    // instance placed inside another keeps answering to its own prefab for as long as it is in the
    // scene, and only becomes flat content when the thing around it is written out.

    [Fact]
    public void PlacingAnInstanceInsideAnotherKeepsItsLink()
    {
        Guid inner = AuthorLeaf("Place_Inner", 7);
        Guid outer = AuthorLeaf("Place_Outer", 1);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        GameObject placed = Inst(inner);
        placed.SetParent(instance);

        Assert.True(placed.IsPrefabInstance);
        Assert.Equal(inner, placed.PrefabAssetId);

        // Its own prefab gives it a source identity, but it is not the outer prefab's structure.
        Assert.False(PrefabUtility.IsProvidedByPrefab(placed));
    }

    [Fact]
    public void ANestedInstanceIsUntouchedByTheOuterRefreshing()
    {
        Guid inner = AuthorLeaf("Ref_NestInner", 7);
        Guid outer = AuthorLeaf("Ref_NestOuter", 1);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);
        Inst(inner).SetParent(instance);

        PrefabUtility.RefreshAllInstances(outer);

        GameObject placed = Assert.Single(instance.Children);
        Assert.Equal(inner, placed.PrefabAssetId);
        Assert.Equal(7, placed.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ANestedInstanceStillFollowsItsOwnPrefab()
    {
        Guid inner = AuthorLeaf("Own_Inner", 7);
        Guid outer = AuthorLeaf("Own_Outer", 1);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);
        Inst(inner).SetParent(instance);

        EditPrefabSource(inner, "Own_Inner.prefab", s => s.GetComponent<OverrideComp>()!.A = 99);

        Assert.Equal(99, instance.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ANestedInstanceIsFlattenedWhenTheOuterIsWrittenOut()
    {
        Guid inner = AuthorLeaf("Write_Inner", 7);

        var outerRoot = new GameObject("Write_Outer");
        LoadSceneWith(outerRoot);
        Inst(inner).SetParent(outerRoot);

        Guid outer = Author(outerRoot, "Write_Outer.prefab");

        // The asset is one self-contained tree, so the content is there and the link is not.
        GameObject fresh = Inst(outer);
        GameObject child = Assert.Single(fresh.Children);
        Assert.Equal(7, child.GetComponent<OverrideComp>()!.A);
        Assert.NotEqual(inner, child.PrefabAssetId);
    }

    #endregion

    #region Shipping

    [Fact]
    public void ANestedLinkInAnOlderAssetIsStrippedForTheBuild()
    {
        // A payload written before flattening was enforced: an instance inside an instance.
        var echo = EchoObject.NewCompound();
        echo["Prefab"] = Link(Guid.NewGuid());

        var child = EchoObject.NewCompound();
        child["Prefab"] = Link(Guid.NewGuid());

        var children = EchoObject.NewList();
        children.ListAdd(child);
        echo["Children"] = children;

        Assert.True(ImportHelper.FlattenNestedPrefabLinks(echo));

        Assert.True(echo.TryGet("Prefab", out _));      // the outer instance is still one
        Assert.False(child.TryGet("Prefab", out _));    // the one inside it is not

        static EchoObject Link(Guid id)
        {
            var link = EchoObject.NewCompound();
            link["AssetId"] = new EchoObject(id.ToString());
            return link;
        }
    }

    #endregion

    #region Assets authored before flattening

    /// <summary>Writes a prefab file directly, the way one authored before flattening would look.</summary>
    private Guid WriteLegacy(GameObject source, string path)
    {
        System.IO.File.WriteAllText(AssetAbsolutePath(path),
            Serializer.Serialize(typeof(object), source).WriteToString());
        return Assets.ImportFile(path);
    }

    private (Guid inner, Guid outer) MakeLegacyNested()
    {
        var innerSource = new GameObject("L_Inner");
        innerSource.AddComponent<OverrideComp>().A = 1;
        Guid inner = CreatePrefabAsset(innerSource, "L_Inner.prefab");

        var outerSource = new GameObject("L_Outer");
        outerSource.AddComponent<OverrideComp>().A = 10;
        Inst(inner).SetParent(outerSource);
        return (inner, WriteLegacy(outerSource, "L_Outer.prefab"));
    }

    [Fact]
    public void AnOlderNestedAssetStillLoadsWithItsContent()
    {
        (Guid inner, Guid outer) = MakeLegacyNested();

        GameObject instance = Inst(outer);

        // Loaded flat: the content is all there, as this prefab's own.
        Assert.Single(instance.Children);
        Assert.NotEqual(inner, instance.Children[0].PrefabAssetId);
        Assert.Equal(1, instance.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ApplyingAnOlderNestedInstanceKeepsItsContent()
    {
        (Guid _, Guid outer) = MakeLegacyNested();

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);
        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.ApplyOverrides(instance);

        // The nested content came from the prefab, so applying must not mistake it for something the
        // instance added and drop it from the asset.
        GameObject fresh = Inst(outer);
        Assert.Equal(42, fresh.GetComponent<OverrideComp>()!.A);
        Assert.Single(fresh.Children);
        Assert.Equal(1, fresh.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void RefreshingAnOlderNestedInstanceKeepsItsContent()
    {
        (Guid _, Guid outer) = MakeLegacyNested();

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        PrefabUtility.RefreshAllInstances(outer);

        Assert.Single(instance.Children);
    }

    [Fact]
    public void ASceneRoundTripKeepsBothLinks()
    {
        (Guid inner, Guid outer) = MakeLegacyNested();

        GameObject instance = Inst(outer);
        Scene scene = LoadSceneWith(instance);

        EchoObject echo = Serializer.Serialize(typeof(object), scene);
        var reloaded = Serializer.Deserialize<Scene>(echo)!;
        Scene.Load(reloaded);
        Scene.ProcessPendingLoad();

        GameObject live = Scene.Current!.AllObjects.First(o => o.Parent == null && o.IsPrefabInstance);
        Assert.Equal(outer, live.PrefabAssetId);
        Assert.Single(live.Children);

        // The older asset was flattened when it was imported, so its content belongs to the outer
        // prefab now rather than to the one it was nested from.
        Assert.NotEqual(inner, live.Children[0].PrefabAssetId);
    }

    #endregion

    #endregion

    #region Api

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

        PrefabUtility.UnpackPrefabInstance(instance);
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

        PrefabUtility.UnpackPrefabInstance(instance);
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

        Assert.True(PrefabUtility.SaveAsPrefabAssetAndConnect(source, "Api_Create.prefab"));

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

            Assert.True(PrefabUtility.SaveAsPrefabAssetAndConnect(source, "Api_Evt.prefab"));

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

    #endregion


    #region Prefab editing mode

    private Guid AuthorSimplePrefab(string path, int value)
    {
        var root = new GameObject("Root");
        root.AddComponent<OverrideComp>().A = value;
        return CreatePrefabAsset(root, path);
    }

    private static void EnterEditingMode(Guid guid)
    {
        PrefabEditingMode.Enter(guid);
        Scene.ProcessPendingLoad();
    }

    private static void ExitEditingMode()
    {
        PrefabEditingMode.Exit();
        Scene.ProcessPendingLoad();
    }

    /// <summary>The prefab root in the editing scene, found the way PrefabEditingMode.Save finds it,
    /// skipping the editor-only camera and light added for visibility.</summary>
    private static GameObject EditingRoot()
        => Scene.Current!.RootObjects.First(go => !go.HideFlags.HasFlag(HideFlags.HideAndDontSave));

    [Fact]
    public void EditingMode_EntersWithThePrefabsContentAndLeavesAgain()
    {
        Guid guid = AuthorSimplePrefab("Mode_Enter.prefab", 5);
        SetSceneCurrent(new GameObject("SceneObject"));

        EnterEditingMode(guid);
        try
        {
            Assert.True(PrefabEditingMode.IsEditing);
            Assert.Equal(guid, PrefabEditingMode.EditingPrefabGuid);
            Assert.Equal(5, EditingRoot().GetComponent<OverrideComp>()!.A);
        }
        finally { ExitEditingMode(); }

        Assert.False(PrefabEditingMode.IsEditing);
    }

    [Fact]
    public void EditingMode_PutsTheOriginalSceneBackOnExit()
    {
        Guid guid = AuthorSimplePrefab("Mode_Restore.prefab", 5);
        SetSceneCurrent(new GameObject("SceneObject"));

        EnterEditingMode(guid);
        Assert.DoesNotContain(Scene.Current!.AllObjects, go => go.Name == "SceneObject");

        ExitEditingMode();

        Assert.Contains(Scene.Current!.AllObjects, go => go.Name == "SceneObject");
    }

    [Fact]
    public void EditingMode_SaveWritesTheEditBackToTheAsset()
    {
        Guid guid = AuthorSimplePrefab("Mode_Save.prefab", 5);
        SetSceneCurrent(new GameObject("SceneObject"));

        EnterEditingMode(guid);
        try
        {
            EditingRoot().GetComponent<OverrideComp>()!.A = 42;
            Assert.True(PrefabEditingMode.Save());
        }
        finally { ExitEditingMode(); }

        Assert.Equal(42, Inst(guid).GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void EditingMode_SaveAndExitDoesBoth()
    {
        Guid guid = AuthorSimplePrefab("Mode_SaveExit.prefab", 5);
        SetSceneCurrent(new GameObject("SceneObject"));

        EnterEditingMode(guid);
        EditingRoot().GetComponent<OverrideComp>()!.A = 7;
        PrefabEditingMode.SaveAndExit();
        Scene.ProcessPendingLoad();

        Assert.False(PrefabEditingMode.IsEditing);
        Assert.Contains(Scene.Current!.AllObjects, go => go.Name == "SceneObject");
        Assert.Equal(7, Inst(guid).GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void EditingMode_ExitingWithoutSavingKeepsTheAssetAsItWas()
    {
        Guid guid = AuthorSimplePrefab("Mode_Discard.prefab", 5);
        SetSceneCurrent(new GameObject("SceneObject"));

        EnterEditingMode(guid);
        EditingRoot().GetComponent<OverrideComp>()!.A = 99;
        ExitEditingMode();

        Assert.Equal(5, Inst(guid).GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void EditingMode_SavingAnEditReachesInstancesInTheScene()
    {
        Guid guid = AuthorSimplePrefab("Mode_Reaches.prefab", 5);
        GameObject instance = Inst(guid);
        SetSceneCurrent(instance);

        EnterEditingMode(guid);
        EditingRoot().GetComponent<OverrideComp>()!.A = 33;
        PrefabEditingMode.SaveAndExit();
        Scene.ProcessPendingLoad();

        GameObject live = Scene.Current!.AllObjects.First(go => go.PrefabAssetId == guid && go.Parent == null);
        Assert.Equal(33, live.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void EditingMode_IsRefusedDuringPlayMode()
    {
        Guid guid = AuthorSimplePrefab("Mode_Play.prefab", 5);
        SetSceneCurrent(new GameObject("SceneObject"));

        Application.IsPlaying = true;
        try
        {
            EnterEditingMode(guid);
            Assert.False(PrefabEditingMode.IsEditing);
        }
        finally { Application.IsPlaying = false; }
    }

    #endregion

    #region Identity across instances

    [Fact]
    public void EveryInstanceGetsItsOwnObjectAndComponentIdentifiers()
    {
        Guid guid = MakePrefab("Ident_Two.prefab");
        GameObject first = Inst(guid);
        GameObject second = Inst(guid);
        LoadSceneWith(first, second);

        Assert.NotEqual(first.Identifier, second.Identifier);
        Assert.NotEqual(first.Children[0].Identifier, second.Children[0].Identifier);
        Assert.NotEqual(
            first.GetComponent<OverrideComp>()!.Identifier,
            second.GetComponent<OverrideComp>()!.Identifier);
    }

    [Fact]
    public void TwoInstancesShareSourceIdentitiesWhileKeepingTheirOwn()
    {
        Guid guid = MakePrefab("Ident_Source.prefab");
        GameObject first = Inst(guid);
        GameObject second = Inst(guid);
        LoadSceneWith(first, second);

        // Where they came from is shared; who they are is not. Overrides resolve on the first fact,
        // every reference into the scene depends on the second.
        Assert.Equal(first.Children[0].SourceIdentifier, second.Children[0].SourceIdentifier);
        Assert.NotEqual(first.Children[0].Identifier, second.Children[0].Identifier);
    }

    [Fact]
    public void IdentifiersStayDistinctAfterARefresh()
    {
        Guid guid = MakePrefab("Ident_Refresh.prefab");
        GameObject first = Inst(guid);
        GameObject second = Inst(guid);
        LoadSceneWith(first, second);

        PrefabUtility.RefreshAllInstances(guid);

        Assert.NotEqual(first.Identifier, second.Identifier);
        Assert.NotEqual(
            first.GetComponent<OverrideComp>()!.Identifier,
            second.GetComponent<OverrideComp>()!.Identifier);
    }

    #endregion

    #region Play mode refuses to change prefabs

    [Fact]
    public void PlayMode_RefusesApplyRevertAndUnpack()
    {
        Guid guid = MakePrefab("Play_Guard.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        Application.IsPlaying = true;
        try
        {
            PrefabUtility.ApplyOverrides(instance);
            PrefabUtility.RevertOverrides(instance);
            PrefabUtility.UnpackPrefabInstance(instance);

            // None of them took effect: still an instance, still overridden, asset untouched.
            Assert.True(instance.IsPrefabInstance);
            Assert.Single(instance.PrefabOverrides);
        }
        finally { Application.IsPlaying = false; }

        Assert.Equal(1, Inst(guid).GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void PlayMode_RefusesToChangeTheOverrideTable()
    {
        Guid guid = MakePrefab("Play_Table.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        Application.IsPlaying = true;
        try
        {
            PrefabUtility.SetPropertyModifications(instance, []);
            Assert.Single(instance.PrefabOverrides);
        }
        finally { Application.IsPlaying = false; }
    }

    #endregion

    #region Identities the asset hands out never churn

    // ---------------------------------------------------------------------
    // Every instance addresses its overrides by the identities the asset holds, so any write to the
    // asset that reassigns one silently unaddresses every other instance's overrides.
    // ---------------------------------------------------------------------

    [Fact]
    public void ApplyingFromOneInstance_KeepsAnotherInstancesOverridesAddressable()
    {
        Guid guid = MakeNestedPrefab("ApplyKeepsOthers.prefab");
        GameObject a = Inst(guid);
        GameObject b = Inst(guid);
        LoadSceneWith(a, b);

        b.GetComponent<OverrideComp>()!.B = 77;
        b.Children[0].GetComponent<OverrideComp>()!.A = 88;
        PrefabUtility.ReconcileInstance(b);

        a.GetComponent<OverrideComp>()!.A = 5;
        PrefabUtility.ReconcileInstance(a);
        PrefabUtility.ApplyOverrides(a);

        Assert.Equal(5, b.GetComponent<OverrideComp>()!.A);   // what A applied reached B
        Assert.Equal(77, b.GetComponent<OverrideComp>()!.B);  // and B kept its own
        Assert.Equal(88, b.Children[0].GetComponent<OverrideComp>()!.A);
        Assert.All(b.PrefabOverrides, o => Assert.True(PrefabUtility.IsOverrideResolvable(b, o.Path)));
    }

    [Fact]
    public void CreatingAPrefabOverAnExistingOne_KeepsInstanceOverridesAddressable()
    {
        Guid guid = MakePrefab("Overwrite.prefab");
        GameObject a = Inst(guid);
        GameObject b = Inst(guid);
        LoadSceneWith(a, b);

        b.GetComponent<OverrideComp>()!.B = 77;
        PrefabUtility.ReconcileInstance(b);

        PrefabUtility.SaveAsPrefabAssetAndConnect(a, "Overwrite.prefab", overwrite: true);
        Assets.Refresh();

        Assert.Equal(77, b.GetComponent<OverrideComp>()!.B);
        Assert.All(b.PrefabOverrides, o => Assert.True(PrefabUtility.IsOverrideResolvable(b, o.Path)));
    }

    [Fact]
    public void ComponentIdentitiesInTheAsset_SurviveAnApply()
    {
        Guid guid = MakePrefab("StableCompIds.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        GameObject before = Inst(guid);
        Guid beforeId = before.GetComponentSourceIdentifier(before.GetComponent<OverrideComp>()!);

        instance.GetComponent<OverrideComp>()!.A = 5;
        PrefabUtility.ReconcileInstance(instance);
        PrefabUtility.ApplyOverrides(instance);

        GameObject after = Inst(guid);
        Guid afterId = after.GetComponentSourceIdentifier(after.GetComponent<OverrideComp>()!);

        Assert.Equal(beforeId, afterId);
    }

    #endregion

    #region A prefab session owns the scene while it lasts

    [Fact]
    public void EditingMode_RefusesToOpenOrCreateAScene()
    {
        Guid guid = MakePrefab("SessionScene.prefab");
        LoadSceneWith(new GameObject("OriginalSceneObject"));

        PrefabEditingMode.Enter(guid);
        Scene.ProcessPendingLoad();

        Assert.False(EditorSceneManager.OpenScene("Anything.scene"));
        EditorSceneManager.NewScene();
        Scene.ProcessPendingLoad();

        // Still the prefab session's own scene, not a replacement.
        Assert.True(PrefabEditingMode.IsEditing);
        Assert.Contains(Scene.Current!.RootObjects, go => go.Name == "Root");

        PrefabEditingMode.Exit();
        Scene.ProcessPendingLoad();
    }

    [Fact]
    public void EditingMode_SaveRefusesWhenItsRootIsNoLongerInTheScene()
    {
        Guid guid = MakePrefab("SessionLost.prefab");
        LoadSceneWith(new GameObject("OriginalSceneObject"));

        PrefabEditingMode.Enter(guid);
        Scene.ProcessPendingLoad();

        // Something swapped the scene without going through the session (a tool, a script).
        var stray = new GameObject("SomeUnrelatedSceneRoot");
        LoadSceneWith(stray);

        Assert.False(PrefabEditingMode.Save());

        // The asset still holds the prefab, not whatever was in the scene.
        Assert.Equal("Root", Inst(guid).Name);

        PrefabEditingMode.Exit();
        Scene.ProcessPendingLoad();
    }

    #endregion

    #region New prefab content never lands on objects the instance owns

    // ---------------------------------------------------------------------
    // Every object the prefab provides gets a counterpart of its own. Nothing the instance owns is
    // ever reused for it, however the two happen to line up by position.
    // ---------------------------------------------------------------------

    [Fact]
    public void AChildAddedToThePrefab_DoesNotSwallowAChildTheInstanceAdded()
    {
        Guid guid = MakeNestedPrefab("SwallowChild.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        var added = new GameObject("InstanceAdded");
        added.AddComponent<VecComp>().V = new Float3(9, 9, 9);
        Scene.Current!.Add(added);
        added.SetParent(instance);
        added.SetSiblingIndex(0); // exactly where the prefab's new child will land

        EditPrefabSource(guid, "SwallowChild.prefab", src =>
        {
            var extra = new GameObject("PrefabExtra");
            extra.SetParent(src);
            extra.SetSiblingIndex(0);
        });
        PrefabUtility.RefreshAllInstances(guid);

        Assert.Contains(instance.Children, c => ReferenceEquals(c, added));
        Assert.Equal("InstanceAdded", added.Name);
        Assert.Equal(9.0, added.GetComponent<VecComp>()!.V.X, 3);
        Assert.Equal(Guid.Empty, added.SourceIdentifier);

        GameObject extraChild = Assert.Single(instance.Children, c => c.Name == "PrefabExtra");
        Assert.NotEqual(Guid.Empty, extraChild.SourceIdentifier);
    }

    [Fact]
    public void AComponentAddedToThePrefab_DoesNotSwallowOneTheInstanceAdded()
    {
        Guid guid = MakePrefab(1, 1, "SwallowComponent.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        VecComp added = instance.AddComponent<VecComp>();
        added.V = new Float3(9, 9, 9);

        EditPrefabSource(guid, "SwallowComponent.prefab",
            src => src.AddComponent<VecComp>().V = new Float3(1, 0, 0));
        PrefabUtility.RefreshAllInstances(guid);

        List<VecComp> vecs = instance.GetComponents<VecComp>().ToList();
        Assert.Equal(2, vecs.Count);
        Assert.Equal(9.0, added.V.X, 3);                                    // the instance's own is untouched
        Assert.Equal(Guid.Empty, instance.GetComponentSourceIdentifier(added));
        Assert.Contains(vecs, v => Math.Abs(v.V.X - 1.0) < 0.001            // and the prefab's arrived beside it
            && instance.GetComponentSourceIdentifier(v) != Guid.Empty);
    }

    [Fact]
    public void AChildAddedToThePrefab_DoesNotSwallowANestedInstance()
    {
        Guid inner = MakeNestedPrefab("SwallowNestedInner.prefab");
        Guid outer = MakeNestedPrefab("SwallowNestedOuter.prefab");

        GameObject outerInstance = Inst(outer);
        LoadSceneWith(outerInstance);

        GameObject nested = Inst(inner);
        Scene.Current!.Add(nested);
        nested.SetParent(outerInstance);
        nested.SetSiblingIndex(0);
        nested.Name = "NestedInstance";

        EditPrefabSource(outer, "SwallowNestedOuter.prefab", src =>
        {
            var extra = new GameObject("OuterExtra");
            extra.SetParent(src);
            extra.SetSiblingIndex(0);
        });
        PrefabUtility.RefreshAllInstances(outer);

        Assert.Equal(inner, nested.PrefabAssetId);
        Assert.Equal("NestedInstance", nested.Name);
        Assert.Contains(outerInstance.Children, c => ReferenceEquals(c, nested));
        Assert.Contains(outerInstance.Children, c => c.Name == "OuterExtra" && c.PrefabAssetId == outer);
    }

    #endregion

    #region Prefab content sits where its prefab put it

    // ---------------------------------------------------------------------
    // The editor refuses to move prefab content, but a script or a tool can. What comes back has to be
    // one object per source identity, whatever route the scene took to get there.
    // ---------------------------------------------------------------------

    [Fact]
    public void ProvidedContentMovedOutOfItsInstance_BecomesAnOrdinaryObject()
    {
        Guid guid = MakeNestedPrefab("MovedOut.prefab");
        GameObject instance = Inst(guid);
        var elsewhere = new GameObject("Elsewhere");
        LoadSceneWith(instance, elsewhere);

        GameObject child = instance.Children[0];
        child.GetComponent<OverrideComp>()!.A = 99;
        PrefabUtility.ReconcileInstance(instance);

        child.SetParent(elsewhere);   // what a tool or a script can do, and the panels cannot refuse
        PrefabUtility.RefreshAllInstances(guid);

        // It keeps its place and its values, and stops claiming to be part of the instance.
        Assert.False(child.IsDisposed);
        Assert.Same(elsewhere, child.Parent);
        Assert.Equal(99, child.GetComponent<OverrideComp>()!.A);
        Assert.False(child.IsPrefabInstance);
        Assert.Equal(Guid.Empty, child.SourceIdentifier);
        Assert.Equal(Guid.Empty, child.GetComponent<OverrideComp>()!.SourceIdentifier);

        // The prefab still provides its own child, in its own place, once.
        GameObject provided = Assert.Single(instance.Children);
        Assert.NotSame(child, provided);
        Assert.NotEqual(Guid.Empty, provided.SourceIdentifier);
    }

    [Fact]
    public void SettlingStrays_LeavesAnInstanceInsideAnotherInstanceAlone()
    {
        Guid inner = MakeNestedPrefab("StrayInner.prefab");
        Guid outer = MakeNestedPrefab("StrayOuter.prefab");

        GameObject outerInstance = Inst(outer);
        LoadSceneWith(outerInstance);
        GameObject nested = Inst(inner);
        Scene.Current!.Add(nested);
        nested.SetParent(outerInstance);

        PrefabUtility.RefreshAllInstances(outer);
        PrefabUtility.RefreshAllInstances(inner);
        PrefabUtility.ReconcileOpenScene();

        // It sits where its own prefab puts it, which is wherever the user put the instance.
        Assert.True(nested.IsPrefabInstance);
        Assert.Equal(inner, nested.PrefabAssetId);
        Assert.Same(outerInstance, nested.Parent);
    }

    #endregion

    #region Baked lighting belongs to the scene, not to the objects

    [Fact]
    public void LightmapPlacements_SurviveASceneRoundTrip()
    {
        var go = new GameObject("Baked");
        go.AddComponent<MeshRenderer>();
        Scene scene = LoadSceneWith(go);

        scene.BakedLighting.Placements[go.Identifier] = new Scene.LightmapPlacement
        {
            Index = 3,
            ScaleOffset = new Float4(0.5f, 0.5f, 0.25f, 0.75f)
        };

        var reloaded = Serializer.Deserialize<Scene>(Serializer.Serialize(typeof(object), scene)!)!;
        try
        {
            GameObject restored = reloaded.RootObjects.First(o => o.Name == "Baked");
            Scene.LightmapPlacement placement = Assert.NotNull(reloaded.BakedLighting.PlacementFor(restored.Identifier));

            Assert.Equal(3, placement.Index);
            Assert.Equal(0.25, placement.ScaleOffset.Z, 3);
        }
        finally { reloaded.Dispose(); }
    }

    [Fact]
    public void BakingAPrefabInstance_RecordsNoOverride_AndSurvivesARefresh()
    {
        var root = new GameObject("Root");
        root.AddComponent<MeshRenderer>();
        Guid guid = CreatePrefabAsset(root, "Baked.prefab");

        GameObject a = Inst(guid);
        GameObject b = Inst(guid);
        Scene scene = LoadSceneWith(a, b);

        // What a bake writes now: one placement per baked object, on the scene.
        scene.BakedLighting.Placements[a.Identifier] = new Scene.LightmapPlacement { Index = 3, ScaleOffset = new Float4(1, 1, 0, 0) };
        scene.BakedLighting.Placements[b.Identifier] = new Scene.LightmapPlacement { Index = 7, ScaleOffset = new Float4(1, 1, 0, 0) };

        PrefabUtility.ReconcileOpenScene();

        // The prefab has no say in it, so there is nothing to record and nothing to hand to anyone else.
        Assert.Empty(a.PrefabOverrides);
        Assert.Empty(b.PrefabOverrides);

        PrefabUtility.RefreshAllInstances(guid);
        Assert.Equal(3, scene.BakedLighting.PlacementFor(a.Identifier)!.Value.Index);
        Assert.Equal(7, scene.BakedLighting.PlacementFor(b.Identifier)!.Value.Index);

        // And applying one instance cannot carry its lighting into the prefab.
        PrefabUtility.ApplyOverrides(a);
        Assert.Equal(7, scene.BakedLighting.PlacementFor(b.Identifier)!.Value.Index);
    }

    [Fact]
    public void ClearingBakedLighting_DropsEveryPlacement()
    {
        var go = new GameObject("Baked");
        Scene scene = LoadSceneWith(go);

        scene.BakedLighting.Placements[go.Identifier] = new Scene.LightmapPlacement { Index = 1, ScaleOffset = new Float4(1, 1, 0, 0) };
        scene.BakedLighting.Lightmaps.Add(default);

        scene.BakedLighting.ClearLightmaps();

        Assert.Empty(scene.BakedLighting.Placements);
        Assert.False(scene.BakedLighting.HasLightmaps);
        Assert.Null(scene.BakedLighting.PlacementFor(go.Identifier));
    }

    #endregion

    #region Where a component came from travels with the component

    [Fact]
    public void ComponentSourceIdentities_SurviveASceneRoundTrip()
    {
        Guid guid = MakeNestedPrefab("CompIdRoundTrip.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        Guid rootSource = instance.GetComponent<OverrideComp>()!.SourceIdentifier;
        Guid childSource = instance.Children[0].GetComponent<OverrideComp>()!.SourceIdentifier;
        Assert.NotEqual(Guid.Empty, rootSource);

        EchoObject saved = Serializer.Serialize(typeof(object), Scene.Current!)!;
        var reloaded = Serializer.Deserialize<Scene>(saved)!;
        try
        {
            GameObject restored = reloaded.RootObjects.First(go => go.PrefabAssetId == guid);

            Assert.Equal(rootSource, restored.GetComponent<OverrideComp>()!.SourceIdentifier);
            Assert.Equal(childSource, restored.Children[0].GetComponent<OverrideComp>()!.SourceIdentifier);

            // Which is what keeps an override addressable across the round trip.
            Assert.Equal(rootSource, restored.GetComponentSourceIdentifier(restored.GetComponent<OverrideComp>()!));
        }
        finally { reloaded.Dispose(); }
    }

    [Fact]
    public void AComponentOnAnOrdinaryObject_WritesNothingAboutPrefabs()
    {
        var plain = new GameObject("Plain");
        plain.AddComponent<OverrideComp>();

        string text = Serializer.Serialize(typeof(object), plain)!.WriteToString();

        Assert.DoesNotContain("_prefabTemplateIdentity", text);
    }

    [Fact]
    public void CopyingAComponentIdentityOntoACopy_TravelsWithTheComponent()
    {
        Guid guid = MakeNestedPrefab("CompIdClone.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        Guid source = instance.GetComponent<OverrideComp>()!.SourceIdentifier;

        // A copy of the whole instance is another instance, so its components say the same thing.
        GameObject dupe = GameObjectClipboard.Duplicate([instance])[0];
        Assert.Equal(source, dupe.GetComponent<OverrideComp>()!.SourceIdentifier);
        Assert.NotEqual(instance.GetComponent<OverrideComp>()!.Identifier,
                        dupe.GetComponent<OverrideComp>()!.Identifier);

        // A copy of part of one is not, so its components say nothing.
        GameObject childCopy = GameObjectClipboard.Duplicate([instance.Children[0]])[0];
        Assert.Equal(Guid.Empty, childCopy.GetComponent<OverrideComp>()!.SourceIdentifier);
    }

    #endregion

    #region A prefab cannot contain another prefab

    [Fact]
    public void DroppingAPrefabIntoAPrefabSession_KeepsItsContentsAndDropsTheLink()
    {
        Guid inner = MakeNestedPrefab("DropInner.prefab");
        GameObject spawned = Inst(inner);

        PrefabUtility.DropPrefabLink(spawned);

        Assert.False(spawned.IsPrefabInstance);
        Assert.Equal(Guid.Empty, spawned.PrefabAssetId);
        Assert.All(spawned.Children, c => Assert.False(c.IsPrefabInstance));

        // The contents are still there, which is the point of flattening rather than refusing.
        Assert.Single(spawned.Children);
        Assert.Equal(2, spawned.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void AnImportDuringAPrefabSession_DropsTheCachedSourceWithoutRefreshing()
    {
        Guid guid = MakePrefab(1, 1, "SessionImport.prefab");
        LoadSceneWith(new GameObject("OriginalSceneObject"));

        PrefabEditingMode.Enter(guid);
        Scene.ProcessPendingLoad();

        // What saving during a session does: rewrite the asset and reimport it.
        GameObject edited = Scene.Current!.RootObjects.First(go => go.Name == "Root");
        edited.GetComponent<OverrideComp>()!.A = 8;
        Assert.True(PrefabEditingMode.Save());

        // The session's scene holds the prefab itself, so there was nothing to refresh, and the
        // comparison baseline now reflects what was just written.
        Assert.Equal(8, Inst(guid).GetComponent<OverrideComp>()!.A);

        PrefabEditingMode.Exit();
        Scene.ProcessPendingLoad();
    }

    #endregion

    #region A copy of part of an instance is not part of an instance

    [Fact]
    public void DuplicatingAProvidedChild_ProducesAPlainObject_NotASecondClaimToOneIdentity()
    {
        Guid guid = MakeNestedPrefab("DupChild.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        GameObject child = instance.Children[0];
        GameObject dupe = GameObjectClipboard.Duplicate([child])[0];

        Assert.False(dupe.IsPrefabInstance);
        Assert.Equal(Guid.Empty, dupe.SourceIdentifier);
        Assert.False(PrefabUtility.IsProvidedByPrefab(dupe));

        // So it is the instance's own: reported as an addition, and left alone by a refresh.
        Assert.Contains(PrefabUtility.DescribeAdditions(instance), a => a.Identifier == dupe.Identifier);

        child.GetComponent<OverrideComp>()!.A = 77;
        PrefabUtility.ReconcileInstance(instance);
        PrefabUtility.RefreshAllInstances(guid);

        Assert.False(dupe.IsDisposed);
        Assert.Equal(77, child.GetComponent<OverrideComp>()!.A);
        Assert.Equal(2, dupe.GetComponent<OverrideComp>()!.A); // the duplicate kept the prefab's value
    }

    [Fact]
    public void DuplicatingAWholeInstance_IsStillAnInstance()
    {
        Guid guid = MakeNestedPrefab("DupWhole.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        GameObject dupe = GameObjectClipboard.Duplicate([instance])[0];

        Assert.True(dupe.IsPrefabInstance);
        Assert.True(PrefabUtility.IsInstanceRoot(dupe));
        Assert.Equal(guid, dupe.PrefabAssetId);
    }

    [Fact]
    public void AnInstancePlacedInsideAnInstanceOfTheSamePrefab_SurvivesAndFollowsItsOwnPrefab()
    {
        Guid guid = MakeNestedPrefab("SelfNested.prefab");
        GameObject outer = Inst(guid);
        GameObject inner = Inst(guid);
        outer.Name = "Outer"; inner.Name = "Inner";
        LoadSceneWith(outer, inner);
        inner.SetParent(outer);

        // It is an instance in its own right, not structure the outer one provides.
        Assert.True(PrefabUtility.IsInstanceRoot(inner));
        Assert.False(PrefabUtility.IsProvidedByPrefab(inner));
        Assert.Same(inner, PrefabUtility.GetPrefabInstanceRoot(inner));

        EditPrefabSource(guid, "SelfNested.prefab", src => src.GetComponent<OverrideComp>()!.B = 4);
        PrefabUtility.RefreshAllInstances(guid);

        Assert.False(inner.IsDisposed);
        Assert.Contains(outer.Children, c => ReferenceEquals(c, inner));
        Assert.Equal(4, inner.GetComponent<OverrideComp>()!.B);  // and it tracked the prefab change
        Assert.Equal(4, outer.GetComponent<OverrideComp>()!.B);
    }

    #endregion

    #region Overrides address one instance, and one apply is one step

    [Fact]
    public void DescribedOverrides_NameTheComponentTheyAreOn_NotOneWithTheSameName()
    {
        Guid guid = MakeNestedPrefab("SameNames.prefab");
        GameObject a = Inst(guid);
        GameObject b = Inst(guid);
        LoadSceneWith(a, b);

        // Both instances have a child called "Child" carrying an OverrideComp, both overridden.
        a.Children[0].GetComponent<OverrideComp>()!.A = 55;
        b.Children[0].GetComponent<OverrideComp>()!.A = 99;
        PrefabUtility.ReconcileInstance(a);
        PrefabUtility.ReconcileInstance(b);

        var described = PrefabUtility.DescribeOverrides(b).Single(d => d.ComponentName == nameof(OverrideComp));

        Assert.Equal(b.Children[0].GetComponent<OverrideComp>()!.Identifier, described.ComponentIdentifier);
        Assert.NotEqual(a.Children[0].GetComponent<OverrideComp>()!.Identifier, described.ComponentIdentifier);
    }

    [Fact]
    public void ApplyComponentOverrides_IsOneUndoStepAndOneWrite()
    {
        Guid guid = MakePrefab(1, 1, "ApplyComponentOnce.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        int writes = 0;
        void Count(Guid _) => writes++;
        PrefabUtility.OnPrefabSaved += Count;
        try
        {
            OverrideComp comp = instance.GetComponent<OverrideComp>()!;
            comp.A = 5; comp.B = 6;
            PrefabUtility.ReconcileInstance(instance);
            Assert.Equal(2, instance.PrefabOverrides.Count);

            Undo.Clear();
            PrefabUtility.ApplyComponentOverrides(instance, comp);
            Undo.FlushFrame();

            Assert.Equal(1, writes);
            Assert.Empty(instance.PrefabOverrides);

            OverrideComp written = Inst(guid).GetComponent<OverrideComp>()!;
            Assert.Equal(5, written.A);
            Assert.Equal(6, written.B);

            // And one undo puts the whole thing back, not half of it.
            Undo.PerformUndo();
            OverrideComp afterUndo = Inst(guid).GetComponent<OverrideComp>()!;
            Assert.Equal(1, afterUndo.A);
            Assert.Equal(1, afterUndo.B);
        }
        finally { PrefabUtility.OnPrefabSaved -= Count; }
    }

    #endregion

    #region Values that belong to the object, not to the prefab

    [Fact]
    public void FieldsTheCloneNeverCopies_AreNeverRecordedAsOverrides()
    {
        var root = new GameObject("Root");
        root.AddComponent<BakedComp>();
        Guid guid = CreatePrefabAsset(root, "PerInstance.prefab");

        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        BakedComp baked = instance.GetComponent<BakedComp>()!;
        baked.BakedValue = 7;    // what a bake writes
        baked.AuthoredValue = 3; // what a user edits
        PrefabUtility.ReconcileInstance(instance);

        PropertyOverride ov = Assert.Single(instance.PrefabOverrides);
        Assert.EndsWith(nameof(BakedComp.AuthoredValue), ov.Path);

        // And a refresh leaves the baked value alone rather than restoring the prefab's.
        PrefabUtility.RefreshAllInstances(guid);
        Assert.Equal(7, instance.GetComponent<BakedComp>()!.BakedValue);
    }

    #endregion

    #region Nested instances keep their own identities

    [Fact]
    public void StabilizingIdentities_DoesNotReachIntoANestedInstance()
    {
        Guid inner = MakeNestedPrefab("StabilizeInner.prefab");

        var outer = new GameObject("Outer");
        GameObject first = Inst(inner);
        GameObject second = Inst(inner);
        LoadSceneWith(outer, first, second);
        first.SetParent(outer);
        second.SetParent(outer);

        PrefabUtility.StabilizeSourceIdentifiers(outer);

        Assert.NotEqual(first.Identifier, second.Identifier);
        Assert.NotEqual(first.Children[0].Identifier, second.Children[0].Identifier);
        Assert.NotEqual(first.GetComponent<OverrideComp>()!.Identifier,
                        second.GetComponent<OverrideComp>()!.Identifier);
    }

    #endregion

    #region A scene that is not open is still its own scene

    [Fact]
    public void RefreshingASceneThatIsNotOpen_ResolvesReferencesInThatScene()
    {
        var root = new GameObject("RefRoot");
        root.AddComponent<RefHolderComp>();
        Guid guid = CreatePrefabAsset(root, "OffScene.prefab");

        GameObject instance = Inst(guid);
        var target = new GameObject("Target");
        LoadSceneWith(instance, target);

        instance.GetComponent<RefHolderComp>()!.Target = target;
        PrefabUtility.ReconcileInstance(instance);
        Assert.Single(instance.PrefabOverrides);

        EchoObject saved = Serializer.Serialize(typeof(object), Scene.Current!)!;

        // A build reads a scene off disk while some other scene is open.
        LoadSceneWith(new GameObject("Elsewhere"));
        var copy = Serializer.Deserialize<Scene>(saved)!;
        try
        {
            PrefabUtility.RefreshInstancesIn(copy);

            GameObject copiedInstance = copy.AllObjects.First(o => o.PrefabAssetId == guid);
            GameObject? resolved = copiedInstance.GetComponent<RefHolderComp>()!.Target;

            Assert.NotNull(resolved);
            Assert.Equal("Target", resolved!.Name);
            Assert.Contains(resolved, copy.AllObjects);   // the copy's own object, not the open scene's
        }
        finally { copy.Dispose(); }
    }

    [Fact]
    public void Build_KeepsThePrefabLinkOnAnInstanceInsideAnotherInstance()
    {
        Guid inner = MakePrefab(1, 1, "BuildInner.prefab");
        Guid outer = MakeNestedPrefab("BuildOuter.prefab");

        GameObject outerInstance = Inst(outer);
        LoadSceneWith(outerInstance);
        GameObject nested = Inst(inner);
        Scene.Current!.Add(nested);
        nested.SetParent(outerInstance);

        EchoObject echo = Serializer.Serialize(typeof(object), Scene.Current!)!;
        Build.BuildPipeline.StripEditorOnlyPrefabData(echo);

        // Both links survive: what an object is an instance of is observable at runtime, so a player
        // must not disagree with play mode about it.
        var reloaded = Serializer.Deserialize<Scene>(echo)!;
        try
        {
            Assert.Contains(reloaded.AllObjects, o => o.PrefabAssetId == outer);
            Assert.Contains(reloaded.AllObjects, o => o.PrefabAssetId == inner);
        }
        finally { reloaded.Dispose(); }
    }

    #endregion

    #region Generated prefabs keep their identities across a reimport

    [Fact]
    public void StabilizedIdentities_AreTheSameForTheSameTreeBuiltTwice()
    {
        static GameObject Build()
        {
            var root = new GameObject("Model");
            root.AddComponent<OverrideComp>();
            var a = new GameObject("Node");
            a.AddComponent<VecComp>();
            a.SetParent(root);
            var b = new GameObject("Node");   // a duplicate name, as a model may well have
            b.SetParent(root);
            return root;
        }

        GameObject first = Build();
        GameObject second = Build();
        EditorModelImporter.StabilizeIdentities(first);
        EditorModelImporter.StabilizeIdentities(second);

        Assert.Equal(first.Identifier, second.Identifier);
        Assert.Equal(first.GetComponent<OverrideComp>()!.Identifier, second.GetComponent<OverrideComp>()!.Identifier);
        Assert.Equal(first.Children[0].Identifier, second.Children[0].Identifier);
        Assert.Equal(first.Children[0].GetComponent<VecComp>()!.Identifier,
                     second.Children[0].GetComponent<VecComp>()!.Identifier);

        // And the two same-named children are still told apart.
        Assert.NotEqual(first.Children[0].Identifier, first.Children[1].Identifier);
    }

    [Fact]
    public void AnAssetRewrittenWithStableIdentities_KeepsInstanceOverrides()
    {
        var authored = new GameObject("Model");
        authored.AddComponent<OverrideComp>().A = 1;
        var child = new GameObject("Node");
        child.AddComponent<OverrideComp>().A = 2;
        child.SetParent(authored);
        EditorModelImporter.StabilizeIdentities(authored);
        Guid guid = WritePrefabFileRaw(authored, "Generated.prefab");

        GameObject instance = Inst(guid);
        LoadSceneWith(instance);
        GameObject liveChild = instance.Children[0];
        liveChild.GetComponent<OverrideComp>()!.A = 99;
        PrefabUtility.ReconcileInstance(instance);

        // Reimport from a freshly built tree, the way an importer rebuilds from its source file.
        var rebuilt = new GameObject("Model");
        rebuilt.AddComponent<OverrideComp>().A = 5;
        var rebuiltChild = new GameObject("Node");
        rebuiltChild.AddComponent<OverrideComp>().A = 2;
        rebuiltChild.SetParent(rebuilt);
        EditorModelImporter.StabilizeIdentities(rebuilt);
        File.WriteAllText(AssetAbsolutePath("Generated.prefab"),
            Serializer.Serialize(typeof(object), rebuilt)!.WriteToString());
        Assets.Reimport(guid);

        Assert.Same(liveChild, instance.Children[0]);        // the same objects, not rebuilt ones
        Assert.Equal(99, liveChild.GetComponent<OverrideComp>()!.A);   // its override survived
        Assert.Equal(5, instance.GetComponent<OverrideComp>()!.A);     // and the new content arrived
    }

    #endregion

    #region Redo repeats an operation rather than recording a new one

    [Fact]
    public void RedoingAnApplyAddition_DoesNotPushAnotherUndoStep()
    {
        Guid guid = MakePrefab(1, 1, "RedoAddition.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.AddComponent<VecComp>();
        var addition = PrefabUtility.DescribeAdditions(instance).First();

        Undo.Clear();
        PrefabUtility.ApplyAddition(instance, addition);
        Undo.FlushFrame();

        Undo.PerformUndo();
        Undo.PerformRedo();
        Undo.FlushFrame();

        // One step in, one step out: redo repeated the apply instead of registering it again.
        Undo.PerformUndo();
        Undo.FlushFrame();
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void InstantiatingAnInstance_CarriesNoDeadIdentityEntries()
    {
        Guid guid = MakeNestedPrefab("NoDeadEntries.prefab");
        GameObject instance = Inst(guid);

        // Every component says where it came from, and none of them is left saying nothing.
        Assert.All(instance.GetComponents<MonoBehaviour>(), c => Assert.NotEqual(Guid.Empty, c.SourceIdentifier));
    }

    #endregion

    #region A deleted prefab stops being a comparison baseline

    [Fact]
    public void DeletingThePrefab_StopsItsInstancesRecordingOverridesAgainstIt()
    {
        Guid guid = MakePrefab("Deleted.prefab");
        GameObject instance = Inst(guid);
        LoadSceneWith(instance);

        instance.GetComponent<OverrideComp>()!.A = 9;
        PrefabUtility.ReconcileInstance(instance);
        Assert.Single(instance.PrefabOverrides);

        Assets.DeleteAsset("Deleted.prefab");

        instance.GetComponent<OverrideComp>()!.B = 7;
        PrefabUtility.ReconcileInstance(instance);

        // Nothing to compare against any more, so nothing new is recorded.
        Assert.Single(instance.PrefabOverrides);
    }

    #endregion
}
