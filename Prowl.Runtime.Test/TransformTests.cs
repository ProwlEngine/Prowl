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
        var t = NewTransform("orbit");
        t.Position = new Float3(1, 0, 0);
        t.RotateAround(new Float3(0, 0, 0), new Float3(0, 1, 0), 90f);

        var p = t.Position;
        // A 90 degree orbit about Y maps (1,0,0) onto the Z axis with unit radius.
        Assert.True(Maths.Abs(p.X) < 0.01, $"X should be ~0 but was {p.X}");
        Assert.True(Maths.Abs(p.Y) < 0.01, $"Y should be ~0 but was {p.Y}");
        Assert.True(Maths.Abs(Maths.Abs(p.Z) - 1f) < 0.01, $"|Z| should be ~1 but was {p.Z}");
    }

    // ---- Basics ----

    [Fact]
    public void NoParent_PositionEqualsLocalPosition()
    {
        var t = NewTransform();
        t.LocalPosition = new Float3(3, 4, 5);
        AssertVec(new Float3(3, 4, 5), t.Position);
    }

    [Fact]
    public void IdentityAxes()
    {
        var t = NewTransform();
        AssertVec(Float3.UnitX, t.Right);
        AssertVec(Float3.UnitY, t.Up);
        AssertVec(Float3.UnitZ, t.Forward);
    }

    [Fact]
    public void TransformPoint_InverseTransformPoint_RoundTrip()
    {
        var t = NewTransform();
        t.LocalPosition = new Float3(5, 2, 3);
        t.LocalRotation = Quaternion.AxisAngle(Float3.Normalize(new Float3(1, 1, 0)), 0.9f);
        t.LocalScale = new Float3(2, 1, 0.5f);

        var p = new Float3(1, 2, 3);
        var back = t.InverseTransformPoint(t.TransformPoint(p));

        AssertVec(p, back, 3);
    }

    [Fact]
    public void TransformDirection_IgnoresPositionAndScale()
    {
        var rot = Quaternion.AxisAngle(Float3.UnitY, 0.7f);
        var t = NewTransform();
        t.LocalPosition = new Float3(100, 50, -20);
        t.LocalScale = new Float3(3, 3, 3);
        t.LocalRotation = rot;

        var d = t.TransformDirection(Float3.UnitX);

        // Scale must not leak in - a direction is rotation-only, so length stays 1 (a scaled impl
        // would give length 3). LengthSquared is an independent check, not derived from the Rotation getter.
        Assert.Equal(1.0, Float3.LengthSquared(d), 4);

        // Position must not leak in - a rotation-only transform yields the identical direction.
        var rotationOnly = NewTransform("rotOnly");
        rotationOnly.LocalRotation = rot;
        AssertVec(rotationOnly.TransformDirection(Float3.UnitX), d, 4);

        // And the rotation was actually applied (guards a "returns input unchanged" bug): X != 1.
        Assert.True(d.X < 0.99f);
    }

    [Fact]
    public void InverseTransformDirection_RoundTrip()
    {
        var t = NewTransform();
        t.LocalRotation = Quaternion.AxisAngle(Float3.Normalize(new Float3(0, 1, 1)), 1.1f);
        var d = Float3.Normalize(new Float3(1, 2, 3));
        AssertVec(d, t.InverseTransformDirection(t.TransformDirection(d)), 3);
    }

    [Fact]
    public void TransformVector_IncludesScale()
    {
        var t = NewTransform();
        t.LocalScale = new Float3(2, 3, 4);
        // No rotation: vector just scales component-wise.
        AssertVec(new Float3(2, 0, 0), t.TransformVector(Float3.UnitX), 4);
        AssertVec(new Float3(0, 3, 0), t.TransformVector(Float3.UnitY), 4);
    }

    // ---- Hierarchy ----

    [Fact]
    public void ChildWorldPosition_FollowsParentTranslation()
    {
        var parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 0, 0);
        var child = NewTransform("Child");
        child.SetParent(parent);
        child.LocalPosition = new Float3(1, 2, 3);

        AssertVec(new Float3(11, 2, 3), child.Position, 4);
    }

    [Fact]
    public void ChildWorldPosition_FollowsParentRotation()
    {
        var parent = NewTransform("Parent");
        parent.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 1.2f);
        var child = NewTransform("Child");
        child.SetParent(parent);
        child.LocalPosition = Float3.UnitX;

        // With parent at origin, scale 1: child world pos == parent.Rotation * localPos == parent.Right.
        AssertVec(parent.Right, child.Position, 4);
    }

    [Fact]
    public void ChildLossyScale_MultipliesParentScale()
    {
        var parent = NewTransform("Parent");
        parent.LocalScale = new Float3(2, 2, 2);
        var child = NewTransform("Child");
        child.SetParent(parent);
        child.LocalScale = new Float3(3, 3, 3);

        AssertVec(new Float3(6, 6, 6), child.LossyScale, 4);
    }

    [Fact]
    public void SetWorldPositionOnChild_PreservedOnReadback()
    {
        var parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 5, 0);
        parent.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 0.6f);
        var child = NewTransform("Child");
        child.SetParent(parent);

        child.Position = new Float3(1, 2, 3);

        AssertVec(new Float3(1, 2, 3), child.Position, 3);
    }

    // ---- Atomic setters ----

    [Fact]
    public void SetPositionAndRotation_WithParent_SetsWorldPose()
    {
        var parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 0, 0);
        parent.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 0.5f);
        var child = NewTransform("Child");
        child.SetParent(parent);

        var rot = Quaternion.AxisAngle(Float3.Normalize(new Float3(0, 1, 1)), 1.0f);
        child.SetPositionAndRotation(new Float3(3, 4, 5), rot);

        AssertVec(new Float3(3, 4, 5), child.Position, 3);
        AssertVec(rot * Float3.UnitZ, child.Forward, 3);
    }

    [Fact]
    public void SetWorldTransform_WithParent_PreservesWorld()
    {
        var parent = NewTransform("Parent");
        parent.LocalPosition = new Float3(10, 0, 0);
        parent.LocalScale = new Float3(2, 2, 2);
        parent.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, 0.4f);
        var child = NewTransform("Child");
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
        var t = NewTransform();
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
        var t = NewTransform();
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
        var root = NewTransform("Root");
        var child = NewTransform("Child");
        var grand = NewTransform("Grand");
        child.SetParent(root);
        grand.SetParent(child);

        Assert.Same(grand, root.Find("Child/Grand"));
        Assert.Same(grand, root.DeepFind("Grand"));
        Assert.Null(root.Find("Child/Missing"));
    }

    [Fact]
    public void IsChildOf_And_SiblingIndex()
    {
        var root = NewTransform("Root");
        var a = NewTransform("A");
        var b = NewTransform("B");
        a.SetParent(root);
        b.SetParent(root);

        Assert.True(a.IsChildOf(root));
        Assert.False(root.IsChildOf(a));
        Assert.Equal(0, a.GetSiblingIndex());
        Assert.Equal(1, b.GetSiblingIndex());

        b.SetAsFirstSibling();
        Assert.Equal(0, b.GetSiblingIndex());
    }
}
