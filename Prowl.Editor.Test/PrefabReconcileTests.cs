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

/// <summary>A component that points at another object, for checking what survives a refresh.</summary>
public sealed class LinkComp : MonoBehaviour
{
    public GameObject? Target;
    public MonoBehaviour? Component;
}

/// <summary>
/// Refreshing an instance updates it in place. It used to be rebuilt from the prefab, which meant a
/// new set of objects: every reference into the instance dangled, and anything the instance had that
/// the prefab did not know about went with the old objects.
/// <para/>
/// These cover what has to survive a refresh (identity, references, per-instance state, instance
/// additions) and what has to follow the prefab (values, added and removed components and children,
/// ordering, nesting).
/// </summary>
public class PrefabReconcileTests : EditorTestHarness
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

}
