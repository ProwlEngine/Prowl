// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo.Cloning;
using Xunit;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Test;

public class GameObjectCloneTests : RuntimeTestBase
{
    private class Marker : MonoBehaviour
    {
        public int Number;
        public string Text = "";
        public GameObject? TargetObject;
        public Marker? TargetComponent;
        public AssetRef<Material> Material;
        public List<GameObject> Many = [];
    }

    private sealed class Required : MonoBehaviour { }

    [RequireComponent(typeof(Required))]
    private sealed class Requires : MonoBehaviour { public int Value; }

    private sealed class Lifecycle : MonoBehaviour
    {
        public static int Enables, Starts;
        public override void OnEnable() => Enables++;
        public override void Start() => Starts++;
    }

    private static GameObject Build(string name, out Marker marker)
    {
        var go = new GameObject(name);
        marker = go.AddComponent<Marker>();
        return go;
    }

    #region Structure

    [Fact]
    public void Clone_CopiesComponentsAndValues()
    {
        GameObject source = Build("root", out Marker marker);
        marker.Number = 7;
        marker.Text = "hello";

        GameObject clone = Cloner.Clone(source);

        Assert.NotSame(source, clone);
        Assert.Equal("root", clone.Name);

        Marker? cloned = clone.GetComponent<Marker>();
        Assert.NotNull(cloned);
        Assert.NotSame(marker, cloned);
        Assert.Equal(7, cloned!.Number);
        Assert.Equal("hello", cloned.Text);
    }

    [Fact]
    public void Clone_ComponentPointsAtItsOwnGameObject()
    {
        GameObject source = Build("root", out _);

        GameObject clone = Cloner.Clone(source);

        Assert.Same(clone, clone.GetComponent<Marker>()!.GameObject);
    }

    [Fact]
    public void Clone_CopiesChildren()
    {
        GameObject source = Build("root", out _);
        GameObject child = Build("child", out Marker childMarker);
        childMarker.Number = 3;
        child.SetParent(source);

        GameObject clone = Cloner.Clone(source);

        Assert.Single(clone.Children);
        Assert.NotSame(child, clone.Children[0]);
        Assert.Equal("child", clone.Children[0].Name);
        Assert.Same(clone, clone.Children[0].Parent);
        Assert.Equal(3, clone.Children[0].GetComponent<Marker>()!.Number);
    }

    [Fact]
    public void Clone_CopiesDeepHierarchy()
    {
        GameObject root = Build("root", out _);
        GameObject middle = Build("middle", out _);
        GameObject leaf = Build("leaf", out Marker leafMarker);
        leafMarker.Number = 9;
        middle.SetParent(root);
        leaf.SetParent(middle);

        GameObject clone = Cloner.Clone(root);

        Assert.Equal(9, clone.Children[0].Children[0].GetComponent<Marker>()!.Number);
        Assert.Equal("leaf", clone.Children[0].Children[0].Name);
    }

    [Fact]
    public void Clone_CopiesTransform()
    {
        GameObject source = Build("root", out _);
        source.Transform.LocalPosition = new Float3(1, 2, 3);
        source.Transform.LocalScale = new Float3(2, 2, 2);

        GameObject clone = Cloner.Clone(source);

        Assert.Equal(new Float3(1, 2, 3), clone.Transform.LocalPosition);
        Assert.Equal(new Float3(2, 2, 2), clone.Transform.LocalScale);
        Assert.NotSame(source.Transform, clone.Transform);
        Assert.Same(clone, clone.Transform.GameObject);
    }

    [Fact]
    public void Clone_CopiesTagLayerAndEnabled()
    {
        GameObject source = Build("root", out _);
        source.LayerIndex = 3;
        source.TagIndex = 2;
        source.Enabled = false;
        source.IsStatic = true;

        GameObject clone = Cloner.Clone(source);

        Assert.Equal(3, clone.LayerIndex);
        Assert.Equal(2, clone.TagIndex);
        Assert.False(clone.Enabled);
        Assert.True(clone.IsStatic);
    }

    [Fact]
    public void Clone_MultipleComponentsOfTheSameType()
    {
        var source = new GameObject("root");
        source.AddComponent<Marker>().Number = 1;
        source.AddComponent<Marker>().Number = 2;

        GameObject clone = Cloner.Clone(source);

        List<Marker> markers = clone.GetComponents<Marker>().ToList();
        Assert.Equal(2, markers.Count);
        Assert.Equal(1, markers[0].Number);
        Assert.Equal(2, markers[1].Number);
    }

    [Fact]
    public void Clone_ComponentIsFindableThroughTheCache()
    {
        GameObject source = Build("root", out _);

        GameObject clone = Cloner.Clone(source);

        Assert.NotNull(clone.GetComponent<Marker>());
        Assert.Single(clone.GetComponents<Marker>());
    }

    #endregion

    #region Identity

    [Fact]
    public void Clone_GetsItsOwnIdentifiers()
    {
        GameObject source = Build("root", out Marker marker);

        GameObject clone = Cloner.Clone(source);

        Assert.NotEqual(source.Identifier, clone.Identifier);
        Assert.NotEqual(source.InstanceID, clone.InstanceID);
        Assert.NotEqual(marker.Identifier, clone.GetComponent<Marker>()!.Identifier);
    }

    [Fact]
    public void Clone_DoesNotCarryTheSourcesAssetIdentity()
    {
        GameObject source = Build("root", out _);
        source.AssetID = Guid.NewGuid();
        source.AssetPath = "Assets/Thing.prefab";

        GameObject clone = Cloner.Clone(source);

        Assert.Equal(Guid.Empty, clone.AssetID);
        Assert.Equal(string.Empty, clone.AssetPath);
    }

    #endregion

    #region References

    [Fact]
    public void Clone_ReferenceIntoTheTreePointsAtTheCopy()
    {
        GameObject source = Build("root", out Marker marker);
        GameObject child = Build("child", out Marker childMarker);
        child.SetParent(source);

        marker.TargetObject = child;
        marker.TargetComponent = childMarker;

        GameObject clone = Cloner.Clone(source);
        Marker cloned = clone.GetComponent<Marker>()!;

        Assert.Same(clone.Children[0], cloned.TargetObject);
        Assert.Same(clone.Children[0].GetComponent<Marker>(), cloned.TargetComponent);
    }

    [Fact]
    public void Clone_ReferenceOutsideTheTreeIsShared()
    {
        GameObject outsider = Build("outsider", out Marker outsiderMarker);
        GameObject source = Build("root", out Marker marker);
        marker.TargetObject = outsider;
        marker.TargetComponent = outsiderMarker;

        GameObject clone = Cloner.Clone(source);
        Marker cloned = clone.GetComponent<Marker>()!;

        Assert.Same(outsider, cloned.TargetObject);
        Assert.Same(outsiderMarker, cloned.TargetComponent);
    }

    [Fact]
    public void Clone_ReferenceToItsOwnRootPointsAtTheCopy()
    {
        GameObject source = Build("root", out Marker marker);
        marker.TargetObject = source;

        GameObject clone = Cloner.Clone(source);

        Assert.Same(clone, clone.GetComponent<Marker>()!.TargetObject);
    }

    [Fact]
    public void Clone_ListOfReferencesIsRemapped()
    {
        GameObject source = Build("root", out Marker marker);
        GameObject a = Build("a", out _);
        GameObject b = Build("b", out _);
        a.SetParent(source);
        b.SetParent(source);
        marker.Many.Add(a);
        marker.Many.Add(b);

        GameObject clone = Cloner.Clone(source);
        Marker cloned = clone.GetComponent<Marker>()!;

        Assert.Equal(2, cloned.Many.Count);
        Assert.Same(clone.Children[0], cloned.Many[0]);
        Assert.Same(clone.Children[1], cloned.Many[1]);
    }

    [Fact]
    public void Clone_AssetReferencesAreShared()
    {
        var material = new Material { Name = "mat" };
        GameObject source = Build("root", out Marker marker);
        marker.Material = material;

        GameObject clone = Cloner.Clone(source);

        Assert.Same(material, clone.GetComponent<Marker>()!.Material.Res);
    }

    #endregion

    #region Independence

    [Fact]
    public void Clone_EditingTheCopyLeavesTheSourceAlone()
    {
        GameObject source = Build("root", out Marker marker);
        marker.Number = 1;

        GameObject clone = Cloner.Clone(source);
        clone.GetComponent<Marker>()!.Number = 99;
        clone.Name = "changed";

        Assert.Equal(1, marker.Number);
        Assert.Equal("root", source.Name);
    }

    [Fact]
    public void Clone_IsDetachedFromAnyScene()
    {
        var scene = CreateScene();
        GameObject source = Build("root", out _);
        scene.Add(source);

        GameObject clone = Cloner.Clone(source);

        Assert.Null(clone.Scene);
        Assert.Null(clone.Parent);
    }

    #endregion

    #region CopyTo

    [Fact]
    public void CopyTo_KeepsTheTargetObjectsAndTheirComponents()
    {
        GameObject source = Build("source", out Marker sourceMarker);
        sourceMarker.Number = 5;

        GameObject target = Build("target", out Marker targetMarker);

        Cloner.CopyTo(source, target);

        Assert.Same(targetMarker, target.GetComponent<Marker>());
        Assert.Equal(5, targetMarker.Number);
        Assert.Equal("source", target.Name);
    }

    [Fact]
    public void CopyTo_KeepsTheTargetsIdentity()
    {
        GameObject source = Build("source", out _);
        GameObject target = Build("target", out Marker targetMarker);

        Guid objectId = target.Identifier;
        Guid componentId = targetMarker.Identifier;

        Cloner.CopyTo(source, target);

        Assert.Equal(objectId, target.Identifier);
        Assert.Equal(componentId, targetMarker.Identifier);
    }

    [Fact]
    public void CopyTo_AddsWhatTheTargetLacks()
    {
        GameObject source = Build("source", out _);
        GameObject sourceChild = Build("child", out _);
        sourceChild.SetParent(source);

        var target = new GameObject("target");

        Cloner.CopyTo(source, target);

        Assert.NotNull(target.GetComponent<Marker>());
        Assert.Single(target.Children);
        Assert.Equal("child", target.Children[0].Name);
        Assert.Same(target, target.Children[0].Parent);
    }

    [Fact]
    public void CopyTo_SeededPairsAreUsed()
    {
        GameObject source = Build("source", out Marker sourceMarker);
        GameObject sourceChild = Build("child", out _);
        sourceChild.SetParent(source);

        GameObject target = Build("target", out Marker targetMarker);
        GameObject targetChild = Build("existing", out _);
        targetChild.SetParent(target);

        var context = new CloneContext();
        context.AddTarget(sourceChild, targetChild);
        context.AddTarget(sourceMarker, targetMarker);

        Cloner.CopyTo(source, target, context);

        Assert.Same(targetChild, target.Children[0]);
        Assert.Same(targetMarker, target.GetComponent<Marker>());
        Assert.Equal("child", targetChild.Name);
    }

    [Fact]
    public void CopyTo_ReferencesLandOnTheTargetsObjects()
    {
        GameObject source = Build("source", out Marker sourceMarker);
        GameObject sourceChild = Build("child", out _);
        sourceChild.SetParent(source);
        sourceMarker.TargetObject = sourceChild;

        GameObject target = Build("target", out _);
        GameObject targetChild = Build("existing", out _);
        targetChild.SetParent(target);

        Cloner.CopyTo(source, target);

        Assert.Same(targetChild, target.GetComponent<Marker>()!.TargetObject);
    }

    [Fact]
    public void CopyTo_LeavesTheTargetInItsScene()
    {
        var scene = CreateScene();
        GameObject source = Build("source", out _);
        GameObject target = Build("target", out _);
        scene.Add(target);

        Cloner.CopyTo(source, target);

        Assert.Same(scene, target.Scene);
    }

    #endregion

    #region Many roots

    [Fact]
    public void CloneAll_ReferencesBetweenRootsLandOnTheCopies()
    {
        GameObject a = Build("a", out Marker markerA);
        GameObject b = Build("b", out Marker markerB);
        markerA.TargetObject = b;
        markerB.TargetObject = a;

        List<GameObject> clones = Cloner.CloneAll([a, b]);

        Assert.Same(clones[1], clones[0].GetComponent<Marker>()!.TargetObject);
        Assert.Same(clones[0], clones[1].GetComponent<Marker>()!.TargetObject);
    }

    [Fact]
    public void Clone_SeparateCallsDoNotCrossLink()
    {
        GameObject a = Build("a", out Marker markerA);
        GameObject b = Build("b", out _);
        markerA.TargetObject = b;

        GameObject cloneA = Cloner.Clone(a);
        GameObject cloneB = Cloner.Clone(b);

        Assert.Same(b, cloneA.GetComponent<Marker>()!.TargetObject);
        Assert.NotSame(cloneB, cloneA.GetComponent<Marker>()!.TargetObject);
    }

    #endregion

    #region Transform cache

    [Fact]
    public void CopyTo_RefreshesAStaleWorldTransform()
    {
        var source = new GameObject("s");
        source.Transform.LocalPosition = new Float3(10, 0, 0);

        var target = new GameObject("t");
        target.Transform.LocalPosition = new Float3(1, 2, 3);
        _ = target.Transform.Position;

        Cloner.CopyTo(source, target);

        Assert.Equal(new Float3(10, 0, 0), target.Transform.Position);
    }

    [Fact]
    public void CopyTo_RefreshesAStaleWorldTransformOnChildren()
    {
        var source = new GameObject("s");
        source.Transform.LocalPosition = new Float3(100, 0, 0);
        var sourceChild = new GameObject("sc");
        sourceChild.SetParent(source);
        sourceChild.Transform.LocalPosition = new Float3(5, 0, 0);

        var target = new GameObject("t");
        var targetChild = new GameObject("tc");
        targetChild.SetParent(target);
        _ = targetChild.Transform.Position;

        Cloner.CopyTo(source, target);

        Assert.Equal(new Float3(105, 0, 0), target.Children[0].Transform.Position);
    }

    #endregion

    #region Components and hierarchy

    [Fact]
    public void Clone_DoesNotAddRequiredComponentsAgain()
    {
        var source = new GameObject("s");
        source.AddComponent<Requires>().Value = 4;
        int before = source.GetComponents<MonoBehaviour>().Count();

        GameObject clone = Cloner.Clone(source);

        Assert.Equal(before, clone.GetComponents<MonoBehaviour>().Count());
        Assert.NotNull(clone.GetComponent<Required>());
        Assert.Equal(4, clone.GetComponent<Requires>()!.Value);
    }

    [Fact]
    public void Clone_CopiesTheEnabledState()
    {
        var source = new GameObject("s");
        source.AddComponent<Marker>().Enabled = false;
        source.Enabled = false;

        GameObject clone = Cloner.Clone(source);

        Assert.False(clone.Enabled);
        Assert.False(clone.GetComponent<Marker>()!.Enabled);
    }

    [Fact]
    public void Clone_KeepsSiblingOrder()
    {
        var root = new GameObject("root");
        foreach (string name in new[] { "a", "b", "c", "d" })
            new GameObject(name).SetParent(root);

        GameObject clone = Cloner.Clone(root);

        Assert.Equal("a,b,c,d", string.Join(",", clone.Children.Select(c => c.Name)));
    }

    [Fact]
    public void Clone_OfAChildComesBackDetached()
    {
        var root = new GameObject("root");
        var child = new GameObject("child");
        child.SetParent(root);

        GameObject clone = Cloner.Clone(child);

        Assert.Null(clone.Parent);
        Assert.Single(root.Children);
    }

    [Fact]
    public void Instantiate_RunsTheLifecycleOnTheCopy()
    {
        var scene = CreateScene();
        Scene.Load(scene);
        Scene.ProcessPendingLoad();

        var original = new GameObject("orig");
        original.AddComponent<Lifecycle>();
        scene.Add(original);
        Tick(scene);

        Lifecycle.Enables = Lifecycle.Starts = 0;

        GameObject? copy = GameObject.Instantiate(original);
        Tick(scene);

        Assert.NotNull(copy);
        Assert.Equal(1, Lifecycle.Enables);
        Assert.Equal(1, Lifecycle.Starts);
    }

    [Fact]
    public void Instantiate_IntoAParentPlacesItRelativeToThatParent()
    {
        var scene = CreateScene();
        Scene.Load(scene);
        Scene.ProcessPendingLoad();

        var parent = new GameObject("parent");
        parent.Transform.LocalPosition = new Float3(10, 0, 0);
        scene.Add(parent);

        var original = new GameObject("orig");
        original.Transform.LocalPosition = new Float3(1, 0, 0);
        scene.Add(original);

        GameObject? copy = GameObject.Instantiate(original, parent, worldPositionStays: false);

        Assert.Same(parent, copy!.Parent);
        Assert.NotNull(copy.Scene);
        Assert.Equal(new Float3(11, 0, 0), copy.Transform.Position);
    }

    #endregion
}
