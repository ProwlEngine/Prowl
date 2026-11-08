// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
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
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");

        bool ok = child.SetParent(parent);

        Assert.True(ok);
        Assert.Same(parent, child.Parent);
        Assert.Contains(child, parent.Children);
        Assert.Equal(1, parent.ChildCount);
    }

    [Fact]
    public void SetParent_Null_Unparents()
    {
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        child.SetParent(null!);

        Assert.Null(child.Parent);
        Assert.DoesNotContain(child, parent.Children);
    }

    [Fact]
    public void SetParent_SameParent_ReturnsTrue_NoDuplicate()
    {
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        bool ok = child.SetParent(parent);

        Assert.True(ok);
        Assert.Single(parent.Children);
    }

    [Fact]
    public void SetParent_Self_ReturnsFalse()
    {
        GameObject go = CreateGameObject();

        Assert.False(go.SetParent(go));
        Assert.Null(go.Parent);
    }

    [Fact]
    public void SetParent_Descendant_ReturnsFalse_PreventsCycle()
    {
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
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
        GameObject p1 = CreateGameObject("P1");
        GameObject p2 = CreateGameObject("P2");
        GameObject child = CreateGameObject("Child");

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
        GameObject parent = CreateGameObject("Parent");
        parent.Transform.Position = new Float3(10, 0, 0);
        GameObject child = CreateGameObject("Child");
        child.Transform.Position = new Float3(0, 0, 0);

        child.SetParent(parent, worldPositionStays: true);

        Assert.Equal(0.0, child.Transform.Position.X, 3);
        Assert.Equal(-10.0, child.Transform.LocalPosition.X, 3);
    }

    [Fact]
    public void SetParent_WorldPositionStaysFalse_KeepsLocalPosition()
    {
        GameObject parent = CreateGameObject("Parent");
        parent.Transform.Position = new Float3(10, 0, 0);
        GameObject child = CreateGameObject("Child");
        child.Transform.LocalPosition = new Float3(0, 0, 0);

        child.SetParent(parent, worldPositionStays: false);

        Assert.Equal(0.0, child.Transform.LocalPosition.X, 3);
        Assert.Equal(10.0, child.Transform.Position.X, 3);
    }

    // ---- Cross-scene transfer ----

    [Fact]
    public void SetParent_AcrossWorlds_OrUnderADetachedParent_IsRefused_MoveToMoves()
    {
        SceneInstance scene1 = CreateScene();
        SceneInstance scene2 = CreateScene();

        GameObject child = CreateGameObject("Child");
        scene1.Add(child);

        GameObject parent = CreateGameObject("Parent");
        scene2.Add(parent);

        Assert.Throws<InvalidOperationException>(() => child.SetParent(parent));
        Assert.Throws<InvalidOperationException>(() => child.SetParent(CreateGameObject("Detached")));
        Assert.Same(scene1, child.Scene);

        child.MoveTo(scene2);
        child.SetParent(parent);

        Assert.Same(scene2, child.Scene);
        Assert.Contains(child, parent.Children);
    }

    // ---- Relationship queries ----

    [Fact]
    public void IsChildOf_And_IsParentOf_AreConsistent()
    {
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        Assert.True(child.IsChildOf(parent));
        Assert.False(parent.IsChildOf(child));
        Assert.True(parent.IsParentOf(child));
        Assert.False(child.IsParentOf(parent));
    }

    [Fact]
    public void IsChildOf_DeepDescendant_ReturnsTrue()
    {
        GameObject root = CreateGameObject("Root");
        GameObject mid = CreateGameObject("Mid");
        GameObject leaf = CreateGameObject("Leaf");
        mid.SetParent(root);
        leaf.SetParent(mid);

        Assert.True(leaf.IsChildOf(root));
        Assert.True(root.IsParentOf(leaf));
    }

    [Fact]
    public void IsChildOrSameTransform_HandlesSelfAndDescendants()
    {
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        Assert.True(GameObject.IsChildOrSameTransform(parent, parent));
        Assert.True(GameObject.IsChildOrSameTransform(child, parent));
        Assert.False(GameObject.IsChildOrSameTransform(parent, child));
    }

    // ---- Descendant enumeration ----

    [Fact]
    public void GetChildrenDeep_ReturnsAllDescendants()
    {
        GameObject root = CreateGameObject("Root");
        GameObject a = CreateGameObject("A");
        GameObject b = CreateGameObject("B");
        GameObject grandchild = CreateGameObject("Grandchild");
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
        GameObject parent = CreateGameObject("Parent");
        GameObject a = CreateGameObject("A");
        GameObject b = CreateGameObject("B");
        GameObject c = CreateGameObject("C");
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
        GameObject go = CreateGameObject();
        Assert.Null(go.GetSiblingIndex());
    }

    // ---- Index path ----

    [Fact]
    public void IndexPath_RoundTrips()
    {
        GameObject root = CreateGameObject("Root");
        GameObject a = CreateGameObject("A");
        GameObject grandchild = CreateGameObject("Grandchild");
        a.SetParent(root);
        grandchild.SetParent(a);

        List<int> path = root.GetIndexPathOfChild(grandchild);

        Assert.Equal([0, 0], path);
        Assert.Same(grandchild, root.GetChildAtIndexPath(path));
    }

    // ---- FindChildByIdentifier ----

    [Fact]
    public void FindChildByIdentifier_DeepVsShallow()
    {
        GameObject root = CreateGameObject("Root");
        GameObject child = CreateGameObject("Child");
        GameObject grandchild = CreateGameObject("Grandchild");
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
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
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
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        child.Enabled = false;

        Assert.False(child.EnabledInHierarchy);
        Assert.True(parent.EnabledInHierarchy);
    }

    [Fact]
    public void EnabledInHierarchy_DisabledChildStaysDisabled_WhenParentReEnabled()
    {
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        child.Enabled = false;
        parent.Enabled = false;
        parent.Enabled = true;

        // Parent is back on, but the child's own flag is still off.
        Assert.False(child.EnabledInHierarchy);
    }
}
