// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.Prefabs;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A component holding a reference to another scene object.</summary>
public sealed class RefHolderComp : MonoBehaviour
{
    public GameObject? Target;
}

/// <summary>A component with a collection field, which cannot be compared by identity.</summary>
public sealed class ListComp : MonoBehaviour
{
    public List<int> Values = [];
}

/// <summary>A component that derives state in OnValidate, as colliders and renderers do.</summary>
public sealed class DerivedStateComp : MonoBehaviour
{
    public int Source;
    [SerializeIgnore] public int Derived;

    public override void OnValidate() => Derived = Source * 2;
}

/// <summary>
/// Locks in the destructive-operation guards: which object an operation is allowed to act on, what a
/// prefab boundary protects, what play mode forbids, and what Apply is not allowed to write back.
/// Every case here corresponds to a reproduced data-loss bug in Design/PrefabAudit.md.
/// </summary>
public class PrefabSafetyTests : EditorTestHarness
{
    private GameObject Inst(Guid guid) => GetPrefab(guid)!.Instantiate()!;

    private void SetSceneCurrent(params GameObject[] instances)
    {
        var scene = new Scene();
        foreach (var i in instances) scene.Add(i);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
    }

    private Guid WritePrefabRaw(GameObject source, string path)
    {
        File.WriteAllText(AssetAbsolutePath(path), Serializer.Serialize(typeof(object), source).WriteToString());
        return Assets.ImportFile(path);
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
        PrefabUtility.DetectComponentOverrides(instance.Children[0], comp);
        SetSceneCurrent(instance);

        PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First().Children[0]);

        var fresh = ((PrefabAsset)AssetDatabase.Get(g)!).Instantiate()!;
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
        PrefabUtility.DetectComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First());

        // Name and placement are per-instance; only the real override reaches the asset.
        var fresh = ((PrefabAsset)AssetDatabase.Get(g)!).Instantiate()!;
        Assert.Equal("Root", fresh.Name);
        Assert.Equal(0.0, fresh.Transform.Position.X, 3);
        Assert.Equal(1.0, fresh.Transform.LocalScale.X, 3);
        Assert.Equal(99, fresh.GetComponent<OverrideComp>()!.A);
    }

    // ---------------------------------------------------------------------
    // Prefab boundaries: nested instances survive operations on their host
    // ---------------------------------------------------------------------

    [Fact]
    public void CreatePrefab_PreservesNestedPrefabLinks()
    {
        var innerSource = new GameObject("Inner");
        innerSource.AddComponent<OverrideComp>().A = 1;
        Guid inner = CreatePrefabAsset(innerSource, "Inner.prefab");

        var parent = new GameObject("Parent");
        Inst(inner).SetParent(parent);
        SetSceneCurrent(parent);

        Assert.True(PrefabUtility.CreatePrefab(parent, "Parent.prefab"));

        // The link survives both in the scene and in the written asset.
        Assert.Equal(inner, parent.Children[0].PrefabAssetId);
        Assert.Contains(inner.ToString(), File.ReadAllText(AssetAbsolutePath("Parent.prefab")));
        Assert.True(parent.IsPrefabInstance);
    }

    [Fact]
    public void BreakPrefabInstance_KeepsNestedInstancesLinked()
    {
        Guid inner = CreatePrefabAsset(new GameObject("Inner"), "Inner.prefab");

        var outerSource = new GameObject("Outer");
        Inst(inner).SetParent(outerSource);
        Guid outer = WritePrefabRaw(outerSource, "Outer.prefab");

        var instance = Inst(outer);
        SetSceneCurrent(instance);

        PrefabUtility.BreakPrefabInstance(instance);

        Assert.False(instance.IsPrefabInstance);
        Assert.Equal(inner, instance.Children[0].PrefabAssetId);
    }

    // ---------------------------------------------------------------------
    // Break is an instance-level operation
    // ---------------------------------------------------------------------

    [Fact]
    public void BreakPrefabInstance_OnChild_UnpacksWholeInstanceAndSurvivesRefresh()
    {
        var root = new GameObject("Root");
        new GameObject("Child").SetParent(root);
        Guid g = CreatePrefabAsset(root, "Br.prefab");

        var instance = Inst(g);
        SetSceneCurrent(instance);

        PrefabUtility.BreakPrefabInstance(instance.Children[0]);

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
        PrefabUtility.DetectGOOverrides(instance);
        SetSceneCurrent(instance);

        PrefabUtility.RefreshAllInstances(g);
        var refreshed = Scene.Current!.RootObjects.First();
        Assert.False(refreshed.Enabled);

        PrefabUtility.RevertSingleOverride(refreshed, "$.Enabled");
        Assert.True(refreshed.Enabled);
        Assert.False(PrefabUtility.IsPropertyOverridden(refreshed, "$.Enabled"));
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
        PrefabUtility.DetectComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, "c0._enabled"));
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
        PrefabUtility.DetectComponentOverrides(instance, comp);

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
        PrefabUtility.DetectComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, "c0.Target"));

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
        PrefabUtility.DetectComponentOverrides(instance, comp);
        var ov = instance.PrefabOverrides.First(o => o.Path == "c0.Target");

        PrefabUtility.ApplySingleOverride(instance, ov);

        // A prefab asset cannot hold a scene reference; it must be dropped, not embedded as a copy.
        string text = File.ReadAllText(AssetAbsolutePath("RefApply.prefab"));
        Assert.DoesNotContain("SceneTarget", text);
        Assert.Null(((PrefabAsset)AssetDatabase.Get(g)!).Instantiate()!.GetComponent<RefHolderComp>()!.Target);
    }

    [Fact]
    public void CreatePrefab_DoesNotEmbedReferencedSceneObjects()
    {
        var target = new GameObject("ExternalTarget");
        var source = new GameObject("Source");
        source.AddComponent<RefHolderComp>().Target = target;
        SetSceneCurrent(source, target);

        Assert.True(PrefabUtility.CreatePrefab(source, "Embed.prefab"));

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
        PrefabUtility.DetectComponentOverrides(instance, comp);

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
        PrefabUtility.DetectComponentOverrides(instance, comp);
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
        Assert.Equal(5, ((PrefabAsset)AssetDatabase.Get(g)!).Instantiate()!.GetComponent<OverrideComp>()!.A);

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

        Assert.Equal(5, ((PrefabAsset)AssetDatabase.Get(g)!).Instantiate()!.GetComponent<OverrideComp>()!.A);
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
        PrefabUtility.DetectComponentOverrides(instance, comp);
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
        PrefabUtility.DetectComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        PrefabUtility.RevertSingleOverride(instance, "c0.Source");

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
        PrefabUtility.DetectComponentOverrides(instance, comp);
        Assert.Empty(instance.PrefabOverrides);

        comp.Values = [1, 2, 4];
        PrefabUtility.DetectComponentOverrides(instance, comp);
        Assert.True(PrefabUtility.IsPropertyOverridden(instance, "c0.Values"));
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
        PrefabUtility.DetectGOOverrides(instance);
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
        PrefabUtility.DetectComponentOverrides(instance, comp);
        SetSceneCurrent(instance);

        Application.IsPlaying = true;
        try
        {
            PrefabUtility.ApplyOverrides(Scene.Current!.RootObjects.First());
            PrefabUtility.BreakPrefabInstance(Scene.Current!.RootObjects.First());
            Assert.False(PrefabUtility.CreatePrefab(new GameObject("X"), "X.prefab"));
        }
        finally
        {
            Application.IsPlaying = false;
        }

        Assert.Equal(1, ((PrefabAsset)AssetDatabase.Get(g)!).Instantiate()!.GetComponent<OverrideComp>()!.A);
        Assert.True(Scene.Current!.RootObjects.First().IsPrefabInstance);
        Assert.False(File.Exists(AssetAbsolutePath("X.prefab")));
    }

    // ---------------------------------------------------------------------
    // CreatePrefab argument validation
    // ---------------------------------------------------------------------

    [Fact]
    public void CreatePrefab_RefusesToClobberAndRejectsNonPrefabPath()
    {
        Assert.True(PrefabUtility.CreatePrefab(new GameObject("A"), "P.prefab"));

        var b = new GameObject("B");
        Assert.False(PrefabUtility.CreatePrefab(b, "P.prefab"));
        Assert.True(PrefabUtility.CreatePrefab(b, "P.prefab", overwrite: true));
        Assert.False(PrefabUtility.CreatePrefab(b, "NotAPrefab"));
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
}
