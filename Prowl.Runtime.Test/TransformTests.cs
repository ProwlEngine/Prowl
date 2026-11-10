// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for <see cref="Transform"/> - the foundational spatial type everything depends on. Assertions
/// are handedness-agnostic (compared via the same rotation/vectors) and avoid raw quaternion equality
/// by checking transformed points/directions instead.
/// </summary>
public class TransformTests : RuntimeTestBase
{
    private static void AssertVec(Float3 expected, Float3 actual, int precision = 4)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }

    private Transform NewTransform(string name = "GO") => CreateGameObject(name).Transform;

    // RotateAround takes DEGREES: the orbit and the spin must use the same unit.
    [Fact]
    public void RotateAround_UsesDegrees()
    {
        Transform t = NewTransform("orbit");
        t.Position = new Float3(1, 0, 0);
        t.RotateAround(new Float3(0, 0, 0), new Float3(0, 1, 0), 90f);

        Float3 p = t.Position;
        // A 90 degree orbit about Y maps (1,0,0) onto the Z axis with unit radius.
        Assert.True(Maths.Abs(p.X) < 0.01, $"X should be ~0 but was {p.X}");
        Assert.True(Maths.Abs(p.Y) < 0.01, $"Y should be ~0 but was {p.Y}");
        Assert.True(Maths.Abs(Maths.Abs(p.Z) - 1f) < 0.01, $"|Z| should be ~1 but was {p.Z}");
    }

    // ---- Basics ----

    [Fact]
    public void NoParent_PositionEqualsLocalPosition()
    {
        Transform t = NewTransform();
        t.LocalPosition = new Float3(3, 4, 5);
        AssertVec(new Float3(3, 4, 5), t.Position);
    }

    [Fact]
    public void IdentityAxes()
    {
        Transform t = NewTransform();
        AssertVec(Float3.UnitX, t.Right);
        AssertVec(Float3.UnitY, t.Up);
        AssertVec(Float3.UnitZ, t.Forward);
    }

    [Fact]
    public void TransformPoint_InverseTransformPoint_RoundTrip()
    {
        Transform t = NewTransform();
        t.LocalPosition = new Float3(5, 2, 3);
        t.LocalRotation = Quaternion.AxisAngle(Float3.Normalize(new Float3(1, 1, 0)), 0.9f);
        t.LocalScale = new Float3(2, 1, 0.5f);

        var p = new Float3(1, 2, 3);
        Float3 back = t.InverseTransformPoint(t.TransformPoint(p));

        AssertVec(p, back, 3);
    }

    [Fact]
    public void TransformDirection_IgnoresPositionAndScale()
    {
        var rot = Quaternion.AxisAngle(Float3.UnitY, 0.7f);
        Transform t = NewTransform();
        t.LocalPosition = new Float3(100, 50, -20);
        t.LocalScale = new Float3(3, 3, 3);
        t.LocalRotation = rot;

        Float3 d = t.TransformDirection(Float3.UnitX);

        // Scale must not leak in - a direction is rotation-only, so length stays 1 (a scaled impl
        // would give length 3). LengthSquared is an independent check, not derived from the Rotation getter.
        Assert.Equal(1.0, Float3.LengthSquared(d), 4);

        // Position must not leak in - a rotation-only transform yields the identical direction.
        Transform rotationOnly = NewTransform("rotOnly");
        rotationOnly.LocalRotation = rot;
        AssertVec(rotationOnly.TransformDirection(Float3.UnitX), d, 4);

        // And the rotation was actually applied (guards a "returns input unchanged" bug): X != 1.
        Assert.True(d.X < 0.99f);
    }

    [Fact]
    public void InverseTransformDirection_RoundTrip()
    {
        Transform t = NewTransform();
        t.LocalRotation = Quaternion.AxisAngle(Float3.Normalize(new Float3(0, 1, 1)), 1.1f);
        var d = Float3.Normalize(new Float3(1, 2, 3));
        AssertVec(d, t.InverseTransformDirection(t.TransformDirection(d)), 3);
    }

    [Fact]
    public void TransformVector_IncludesScale()
    {
        Transform t = NewTransform();
        t.LocalScale = new Float3(2, 3, 4);
        // No rotation: vector just scales component-wise.
        AssertVec(new Float3(2, 0, 0), t.TransformVector(Float3.UnitX), 4);
        AssertVec(new Float3(0, 3, 0), t.TransformVector(Float3.UnitY), 4);
    }

    // ---- Hierarchy ----

    [Fact]
    public void ChildWorldPosition_FollowsParentTranslation()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 0, 0);
        Transform child = NewTransform("Child");
        child.SetParent(parent);
        child.LocalPosition = new Float3(1, 2, 3);

        AssertVec(new Float3(11, 2, 3), child.Position, 4);
    }

    [Fact]
    public void ChildWorldPosition_FollowsParentRotation()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 1.2f);
        Transform child = NewTransform("Child");
        child.SetParent(parent);
        child.LocalPosition = Float3.UnitX;

        // With parent at origin, scale 1: child world pos == parent.Rotation * localPos == parent.Right.
        AssertVec(parent.Right, child.Position, 4);
    }

    [Fact]
    public void ChildLossyScale_MultipliesParentScale()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalScale = new Float3(2, 2, 2);
        Transform child = NewTransform("Child");
        child.SetParent(parent);
        child.LocalScale = new Float3(3, 3, 3);

        AssertVec(new Float3(6, 6, 6), child.LossyScale, 4);
    }

    [Fact]
    public void SetWorldPositionOnChild_PreservedOnReadback()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 5, 0);
        parent.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 0.6f);
        Transform child = NewTransform("Child");
        child.SetParent(parent);

        child.Position = new Float3(1, 2, 3);

        AssertVec(new Float3(1, 2, 3), child.Position, 3);
    }

    // ---- Atomic setters ----

    [Fact]
    public void SetPositionAndRotation_WithParent_SetsWorldPose()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 0, 0);
        parent.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 0.5f);
        Transform child = NewTransform("Child");
        child.SetParent(parent);

        var rot = Quaternion.AxisAngle(Float3.Normalize(new Float3(0, 1, 1)), 1.0f);
        child.SetPositionAndRotation(new Float3(3, 4, 5), rot);

        AssertVec(new Float3(3, 4, 5), child.Position, 3);
        AssertVec(rot * Float3.UnitZ, child.Forward, 3);
    }

    [Fact]
    public void SetWorldTransform_WithParent_PreservesWorld()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 0, 0);
        parent.LocalScale = new Float3(2, 2, 2);
        parent.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 0.4f);
        Transform child = NewTransform("Child");
        child.SetParent(parent);

        var rot = Quaternion.AxisAngle(Float3.Normalize(new Float3(1, 1, 0)), 0.8f);
        child.SetWorldTransform(new Float3(3, 4, 5), rot, new Float3(1, 2, 3));

        AssertVec(new Float3(3, 4, 5), child.Position, 3);
        AssertVec(new Float3(1, 2, 3), child.LossyScale, 2);
        AssertVec(rot * Float3.UnitZ, child.Forward, 3);
    }

    // ---- LookAt ----

    [Fact]
    public void LookAt_PointsForwardAtTarget()
    {
        Transform t = NewTransform();
        t.LocalPosition = Float3.Zero;
        t.LookAt(new Float3(0, 0, 5));
        AssertVec(Float3.UnitZ, t.Forward, 3);

        t.LookAt(new Float3(5, 0, 0));
        AssertVec(Float3.UnitX, t.Forward, 3);
    }

    // ---- Change detection ----

    [Fact]
    public void Version_IncrementsOnChange()
    {
        Transform t = NewTransform();
        uint last = t.Version;
        t.LocalPosition = new Float3(1, 0, 0);
        Assert.NotEqual(last, t.Version);
        Assert.True(t.HasChanged(ref last));
        Assert.False(t.HasChanged(ref last)); // no change since last check
    }

    // ---- Find / hierarchy queries ----

    [Fact]
    public void Find_And_DeepFind_ByName()
    {
        Transform root = NewTransform("Root");
        Transform child = NewTransform("Child");
        Transform grand = NewTransform("Grand");
        child.SetParent(root);
        grand.SetParent(child);

        Assert.Same(grand, root.Find("Child/Grand"));
        Assert.Same(grand, root.DeepFind("Grand"));
        Assert.Null(root.Find("Child/Missing"));
    }

    [Fact]
    public void IsChildOf_And_SiblingIndex()
    {
        Transform root = NewTransform("Root");
        Transform a = NewTransform("A");
        Transform b = NewTransform("B");
        a.SetParent(root);
        b.SetParent(root);

        Assert.True(a.IsChildOf(root));
        Assert.False(root.IsChildOf(a));
        Assert.Equal(0, a.GetSiblingIndex());
        Assert.Equal(1, b.GetSiblingIndex());

        b.SetAsFirstSibling();
        Assert.Equal(0, b.GetSiblingIndex());
    }

    // ---- World matrix lazy cache ----
    // LocalToWorldMatrix is cached and only rebuilt when this transform's Version or an ancestor's
    // world matrix changes. These cover the invalidation paths a stale cache would get wrong.

    private static Float3 WorldOrigin(Transform t) => Float4x4.TransformPoint(Float3.Zero, t.LocalToWorldMatrix);

    // World-space forward derived purely from the cached matrix (origin subtracted so translation drops out).
    // Used to catch a matrix cache that lags behind the rotation the Rotation getter already reflects.
    private static Float3 WorldForward(Transform t) =>
        Float4x4.TransformPoint(Float3.UnitZ, t.LocalToWorldMatrix) - Float4x4.TransformPoint(Float3.Zero, t.LocalToWorldMatrix);

    [Fact]
    public void WorldMatrix_SelfMove_InvalidatesCache()
    {
        Transform t = NewTransform("t");
        t.LocalPosition = new Float3(1, 2, 3);
        AssertVec(new Float3(1, 2, 3), WorldOrigin(t));
        AssertVec(new Float3(1, 2, 3), WorldOrigin(t)); // cache hit, same result

        t.LocalPosition = new Float3(5, 6, 7);
        AssertVec(new Float3(5, 6, 7), WorldOrigin(t));
    }

    // The critical case: prime a child's cache, then move the PARENT (which never touches the child's
    // own Version). The child must still refresh via the parent's world-version.
    [Fact]
    public void WorldMatrix_AncestorMove_InvalidatesChild()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 0, 0);
        Transform child = NewTransform("Child");
        child.SetParent(parent, false);
        child.LocalPosition = new Float3(1, 2, 3);

        AssertVec(new Float3(11, 2, 3), WorldOrigin(child)); // prime cache

        parent.LocalPosition = new Float3(20, 0, 0);
        AssertVec(new Float3(21, 2, 3), WorldOrigin(child));
    }

    // Two levels deep: moving the root must cascade all the way to the leaf.
    [Fact]
    public void WorldMatrix_RootMove_InvalidatesGrandchild()
    {
        Transform root = NewTransform("Root");
        Transform mid = NewTransform("Mid");
        Transform leaf = NewTransform("Leaf");
        mid.SetParent(root, false);
        leaf.SetParent(mid, false);
        root.LocalPosition = new Float3(0, 100, 0);
        mid.LocalPosition = new Float3(0, 10, 0);
        leaf.LocalPosition = new Float3(0, 1, 0);

        AssertVec(new Float3(0, 111, 0), WorldOrigin(leaf)); // prime whole chain

        root.LocalPosition = new Float3(0, 200, 0);
        AssertVec(new Float3(0, 211, 0), WorldOrigin(leaf));
    }

    // Reparenting keeps the local values (worldPositionStays: false) but must refresh the cached world.
    [Fact]
    public void WorldMatrix_Reparent_InvalidatesCache()
    {
        Transform a = NewTransform("A");
        a.LocalPosition = new Float3(10, 0, 0);
        Transform b = NewTransform("B");
        b.LocalPosition = new Float3(100, 0, 0);
        Transform child = NewTransform("Child");
        child.SetParent(a, false);
        child.LocalPosition = new Float3(1, 0, 0);

        AssertVec(new Float3(11, 0, 0), WorldOrigin(child)); // prime cache under A

        child.SetParent(b, false);
        AssertVec(new Float3(101, 0, 0), WorldOrigin(child));
    }

    // Parent and child both move between reads: guards a cache that tracks only one of the two versions.
    [Fact]
    public void WorldMatrix_ParentThenChildMove_Composes()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 0, 0);
        Transform child = NewTransform("Child");
        child.SetParent(parent, false);
        child.LocalPosition = new Float3(1, 0, 0);

        AssertVec(new Float3(11, 0, 0), WorldOrigin(child));

        parent.LocalPosition = new Float3(20, 0, 0);
        child.LocalPosition = new Float3(2, 0, 0);
        AssertVec(new Float3(22, 0, 0), WorldOrigin(child));
    }

    // ---- Rotate / RotateAround cache invalidation ----
    // Rotate(axis, angle) and RotateAround both funnel through RotateAroundInternal, which writes
    // _localRotation directly. The Rotation/Forward getters read that field so they update, but the
    // versioned world-matrix cache (LocalToWorldMatrix, Position, TransformPoint, every child's world)
    // keys off Version - so if the spin doesn't bump Version, the visual/physics world silently lags.
    // These exercise that split from several angles; each one is wrong before the Version bump is in.

    // The headline bug: spin a parent, its child's world position must move. The parent's Version never
    // touches the child, so this only works if the parent's spin bumps its own Version (-> world-version).
    [Fact]
    public void ParentRotateAxisAngle_MovesChildWorldPosition()
    {
        Transform parent = NewTransform("Parent");
        Transform child = NewTransform("Child");
        child.SetParent(parent, false);
        child.LocalPosition = new Float3(0, 0, 1);

        AssertVec(new Float3(0, 0, 1), child.Position); // prime both caches

        parent.Rotate(Float3.UnitY, 90f);

        Float3 after = child.Position;
        Assert.True(Float3.LengthSquared(after - new Float3(0, 0, 1)) > 0.25,
            $"Child world position must change when the parent spins, but stayed at {after}");
        // (0,0,1) turned 90 deg about Y lands on the X axis (handedness-agnostic: |X|~1, Y/Z ~0).
        Assert.True(Maths.Abs(after.Y) < 0.01, $"Y ~0 expected, got {after}");
        Assert.True(Maths.Abs(after.Z) < 0.01, $"Z ~0 expected, got {after}");
        Assert.True(Maths.Abs(Maths.Abs(after.X) - 1f) < 0.01, $"|X| ~1 expected, got {after}");
    }

    // RotateAround about the parent's OWN centre: the internal Position write is a no-op (the pivot is the
    // parent itself), so only the rotation changes - and that rotation alone must still dirty the child.
    [Fact]
    public void ParentRotateAround_OwnCentre_MovesChildWorldPosition()
    {
        Transform parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(5, 0, 0);
        Transform child = NewTransform("Child");
        child.SetParent(parent, false);
        child.LocalPosition = new Float3(0, 0, 2);

        Float3 before = child.Position; // (5,0,2), primes caches
        AssertVec(new Float3(5, 0, 2), before);

        parent.RotateAround(parent.Position, Float3.UnitY, 90f);

        Float3 after = child.Position;
        Assert.True(Float3.LengthSquared(after - before) > 0.25,
            $"Child must orbit the parent's centre, but stayed at {after}");
        // Parent stays at (5,0,0); the (0,0,2) offset swings onto X, so world X = 5 +/- 2, Y/Z ~0.
        Assert.True(Maths.Abs(after.Y) < 0.01, $"Y ~0 expected, got {after}");
        Assert.True(Maths.Abs(after.Z) < 0.01, $"Z ~0 expected, got {after}");
        Assert.True(Maths.Abs(Maths.Abs(after.X - 5f) - 2f) < 0.01, $"world X should be 5 +/- 2, got {after}");
    }

    // The matrix and the Rotation getter must never disagree. Rotation reads _localRotation live; the
    // matrix is cached - so after a spin, world-forward computed each way has to match (position untouched).
    [Fact]
    public void RotateAxisAngle_KeepsMatrixConsistentWithRotationGetter()
    {
        Transform t = NewTransform();
        t.LocalPosition = new Float3(2, 3, 4);
        _ = t.LocalToWorldMatrix; // prime the cache while rotation is still identity

        t.Rotate(Float3.UnitX, 90f);

        AssertVec(t.Forward, WorldForward(t), 3);
        AssertVec(new Float3(2, 3, 4), WorldOrigin(t), 3); // pure rotation must not shift position
    }

    // Consecutive spins with a cache build in between: the second spin has to dirty the just-built cache,
    // otherwise it's dropped and two 45s stop equalling one 90 when read through the matrix.
    [Fact]
    public void RotateAxisAngle_SecondSpinAfterCacheBuild_IsNotDropped()
    {
        Transform incremental = NewTransform("inc");
        incremental.Rotate(Float3.UnitY, 45f);
        _ = incremental.LocalToWorldMatrix;    // build the cache at 45 deg
        incremental.Rotate(Float3.UnitY, 45f); // must invalidate that cache

        Transform oneShot = NewTransform("one");
        oneShot.Rotate(Float3.UnitY, 90f);

        AssertVec(WorldForward(oneShot), WorldForward(incremental), 3);
    }

    // Deepest-leaf case: spinning the root must cascade two levels down to the leaf's world position.
    [Fact]
    public void DeepChain_RotateRoot_MovesLeafWorldPosition()
    {
        Transform root = NewTransform("Root");
        Transform mid = NewTransform("Mid");
        Transform leaf = NewTransform("Leaf");
        mid.SetParent(root, false);
        leaf.SetParent(mid, false);
        leaf.LocalPosition = new Float3(0, 0, 3);

        AssertVec(new Float3(0, 0, 3), leaf.Position); // prime the whole chain

        root.Rotate(Float3.UnitY, 90f);

        Float3 after = leaf.Position;
        Assert.True(Maths.Abs(Maths.Abs(after.X) - 3f) < 0.01, $"|X| ~3 expected, got {after}");
        Assert.True(Maths.Abs(after.Z) < 0.01, $"Z ~0 expected, got {after}");
    }

    // Change detection is how renderers/animation skip idle transforms; a spin that doesn't register as a
    // change means stale draws. Covers both entry points into RotateAroundInternal.
    [Fact]
    public void RotateAndRotateAround_RegisterAsChanged()
    {
        Transform t = NewTransform();
        uint watch = t.Version;

        t.Rotate(Float3.UnitY, 30f);
        Assert.True(t.HasChanged(ref watch), "Rotate(axis, angle) must register as a change");

        t.RotateAround(t.Position, Float3.UnitX, 30f); // pivot = self, so only rotation changes
        Assert.True(t.HasChanged(ref watch), "RotateAround about own centre must register as a change");
    }

    // Correctness guard for both Rotate spellings: the euler and axis-angle paths must agree, so a fix to
    // one can't quietly diverge from the other (guards wrong axis/order regressions).
    [Fact]
    public void RotateEuler_AndAxisAngle_Agree()
    {
        Transform byEuler = NewTransform("euler");
        Transform byAxis = NewTransform("axis");
        byEuler.Rotate(new Float3(0, 90f, 0));
        byAxis.Rotate(Float3.UnitY, 90f);

        AssertVec(byEuler.Forward, byAxis.Forward, 3);
        AssertVec(byEuler.Right, byAxis.Right, 3);
    }
}
