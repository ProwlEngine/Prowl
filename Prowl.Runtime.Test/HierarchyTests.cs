// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for GameObject hierarchy: SetParent (including the cycle guard, worldPositionStays math and
/// cross-scene transfer), child/parent queries, sibling indexing, and EnabledInHierarchy propagation.
/// </summary>
public class HierarchyTests : RuntimeTestBase
{
    // ---- SetParent basics ----

    [Fact]
    public void SetParent_SetsParentAndRegistersChild()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");

        bool ok = child.SetParent(parent);

        Assert.True(ok);
        Assert.Same(parent, child.Parent);
        Assert.Contains(child, parent.Children);
        Assert.Equal(1, parent.ChildCount);
    }

    [Fact]
    public void SetParent_Null_Unparents()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        child.SetParent(null!);

        Assert.Null(child.Parent);
        Assert.DoesNotContain(child, parent.Children);
    }

    [Fact]
    public void SetParent_SameParent_ReturnsTrue_NoDuplicate()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        bool ok = child.SetParent(parent);

        Assert.True(ok);
        Assert.Single(parent.Children);
    }

    [Fact]
    public void SetParent_Self_ReturnsFalse()
    {
        var go = CreateGameObject();

        Assert.False(go.SetParent(go));
        Assert.Null(go.Parent);
    }

    [Fact]
    public void SetParent_Descendant_ReturnsFalse_PreventsCycle()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        // Re-parenting the parent under its own child would create a cycle.
        bool ok = parent.SetParent(child);

        Assert.False(ok);
        Assert.Null(parent.Parent);
        Assert.Same(parent, child.Parent);
    }

    [Fact]
    public void SetParent_Reparent_RemovesFromOldParent()
    {
        var p1 = CreateGameObject("P1");
        var p2 = CreateGameObject("P2");
        var child = CreateGameObject("Child");

        child.SetParent(p1);
        child.SetParent(p2);

        Assert.DoesNotContain(child, p1.Children);
        Assert.Contains(child, p2.Children);
        Assert.Same(p2, child.Parent);
    }

    // ---- worldPositionStays ----

    [Fact]
    public void SetParent_WorldPositionStays_PreservesWorldPosition()
    {
        var parent = CreateGameObject("Parent");
        parent.Transform.Position = new Float3(10, 0, 0);
        var child = CreateGameObject("Child");
        child.Transform.Position = new Float3(0, 0, 0);

        child.SetParent(parent, worldPositionStays: true);

        Assert.Equal(0.0, child.Transform.Position.X, 3);
        Assert.Equal(-10.0, child.Transform.LocalPosition.X, 3);
    }

    [Fact]
    public void SetParent_WorldPositionStaysFalse_KeepsLocalPosition()
    {
        var parent = CreateGameObject("Parent");
        parent.Transform.Position = new Float3(10, 0, 0);
        var child = CreateGameObject("Child");
        child.Transform.LocalPosition = new Float3(0, 0, 0);

        child.SetParent(parent, worldPositionStays: false);

        Assert.Equal(0.0, child.Transform.LocalPosition.X, 3);
        Assert.Equal(10.0, child.Transform.Position.X, 3);
    }

    // ---- Cross-scene transfer ----

    [Fact]
    public void SetParent_AcrossScenes_MovesChildToParentScene()
    {
        var scene1 = CreateScene();
        var scene2 = CreateScene();

        var child = CreateGameObject("Child");
        scene1.Add(child);

        var parent = CreateGameObject("Parent");
        scene2.Add(parent);

        child.SetParent(parent);

        Assert.Same(scene2, child.Scene);
        Assert.Contains(child, parent.Children);
    }

    // ---- Relationship queries ----

    [Fact]
    public void IsChildOf_And_IsParentOf_AreConsistent()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        Assert.True(child.IsChildOf(parent));
        Assert.False(parent.IsChildOf(child));
        Assert.True(parent.IsParentOf(child));
        Assert.False(child.IsParentOf(parent));
    }

    [Fact]
    public void IsChildOf_DeepDescendant_ReturnsTrue()
    {
        var root = CreateGameObject("Root");
        var mid = CreateGameObject("Mid");
        var leaf = CreateGameObject("Leaf");
        mid.SetParent(root);
        leaf.SetParent(mid);

        Assert.True(leaf.IsChildOf(root));
        Assert.True(root.IsParentOf(leaf));
    }

    [Fact]
    public void IsChildOrSameTransform_HandlesSelfAndDescendants()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        Assert.True(GameObject.IsChildOrSameTransform(parent, parent));
        Assert.True(GameObject.IsChildOrSameTransform(child, parent));
        Assert.False(GameObject.IsChildOrSameTransform(parent, child));
    }

    // ---- Descendant enumeration ----

    [Fact]
    public void GetChildrenDeep_ReturnsAllDescendants()
    {
        var root = CreateGameObject("Root");
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");
        var grandchild = CreateGameObject("Grandchild");
        a.SetParent(root);
        b.SetParent(root);
        grandchild.SetParent(a);

        var deep = root.GetChildrenDeep().ToList();

        Assert.Equal(3, deep.Count);
        Assert.Contains(a, deep);
        Assert.Contains(b, deep);
        Assert.Contains(grandchild, deep);
    }

    // ---- Sibling index ----

    [Fact]
    public void SiblingIndex_GetAndSet()
    {
        var parent = CreateGameObject("Parent");
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");
        var c = CreateGameObject("C");
        a.SetParent(parent);
        b.SetParent(parent);
        c.SetParent(parent);

        Assert.Equal(0, a.GetSiblingIndex());
        Assert.Equal(2, c.GetSiblingIndex());

        c.SetSiblingIndex(0);

        Assert.Equal(0, c.GetSiblingIndex());
        Assert.Equal(1, a.GetSiblingIndex());
    }

    [Fact]
    public void GetSiblingIndex_NoParent_ReturnsNull()
    {
        var go = CreateGameObject();
        Assert.Null(go.GetSiblingIndex());
    }

    // ---- Index path ----

    [Fact]
    public void IndexPath_RoundTrips()
    {
        var root = CreateGameObject("Root");
        var a = CreateGameObject("A");
        var grandchild = CreateGameObject("Grandchild");
        a.SetParent(root);
        grandchild.SetParent(a);

        var path = root.GetIndexPathOfChild(grandchild);

        Assert.Equal([0, 0], path);
        Assert.Same(grandchild, root.GetChildAtIndexPath(path));
    }

    // ---- FindChildByIdentifier ----

    [Fact]
    public void FindChildByIdentifier_DeepVsShallow()
    {
        var root = CreateGameObject("Root");
        var child = CreateGameObject("Child");
        var grandchild = CreateGameObject("Grandchild");
        child.SetParent(root);
        grandchild.SetParent(child);

        Assert.Same(child, root.FindChildByIdentifier(child.Identifier));
        Assert.Same(grandchild, root.FindChildByIdentifier(grandchild.Identifier, deep: true));
        Assert.Null(root.FindChildByIdentifier(grandchild.Identifier, deep: false));
    }

    // ---- EnabledInHierarchy propagation ----

    [Fact]
    public void EnabledInHierarchy_FollowsParentState()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        Assert.True(child.EnabledInHierarchy);

        parent.Enabled = false;
        Assert.False(child.EnabledInHierarchy);
        Assert.True(child.Enabled); // own flag is untouched

        parent.Enabled = true;
        Assert.True(child.EnabledInHierarchy);
    }

    [Fact]
    public void EnabledInHierarchy_ChildDisabled_DoesNotAffectParent()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        child.Enabled = false;

        Assert.False(child.EnabledInHierarchy);
        Assert.True(parent.EnabledInHierarchy);
    }

    [Fact]
    public void EnabledInHierarchy_DisabledChildStaysDisabled_WhenParentReEnabled()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        child.Enabled = false;
        parent.Enabled = false;
        parent.Enabled = true;

        // Parent is back on, but the child's own flag is still off.
        Assert.False(child.EnabledInHierarchy);
    }
}
