// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

using Silk.NET.OpenXR;

using Xunit;

using Quaternion = Prowl.Vector.Quaternion;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tracking without a headset: the conversions and the recentring maths the session relies on, and every tracking
/// query reading as untracked while XR is not running.
/// </summary>
public class XRTrackingTests
{
    private static void AssertClose(Float3 expected, Float3 actual, float tolerance = 1e-4f)
        => Assert.True(Float3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    private static Quaternion AsNumbers(Quaternionf q) => new(q.X, q.Y, q.Z, q.W);

    // The joints are read straight from the runtime's array, so they have to be in its order.
    [Fact]
    public void HandJoints_FollowOpenXRsOrder()
    {
        foreach (XRHandJoint joint in Enum.GetValues<XRHandJoint>())
            Assert.Equal((int)Enum.Parse<HandJointEXT>(joint + "Ext"), (int)joint);
        Assert.Equal((int)HandJointEXT.LittleTipExt, (int)XRHandJoint.LittleTip);
    }

    // Turning at a converted angular velocity has to land where turning in OpenXR space then converting does.
    [Theory]
    [InlineData(0.3f, -1.2f, 0.7f)]
    [InlineData(2f, 0f, 0f)]
    [InlineData(0f, 0f, -3f)]
    public void AngularVelocity_ConvertsLikeTheTurnItMakes(float x, float y, float z)
    {
        var xrSpin = new Vector3f(x, y, z);
        float seconds = 0.1f;
        Float3 axis = new(x, y, z);
        float speed = Float3.Length(axis);

        Quaternion xrTurn = Quaternion.AxisAngle(axis / speed, speed * seconds);
        Quaternion expected = OpenXRSession.ToProwl(new Quaternionf(xrTurn.X, xrTurn.Y, xrTurn.Z, xrTurn.W));

        Float3 spin = OpenXRSession.ToProwlAngular(xrSpin);
        Quaternion actual = Quaternion.AxisAngle(spin / Float3.Length(spin), Float3.Length(spin) * seconds);

        Assert.True(MathF.Abs(Quaternion.Dot(expected, actual)) > 1f - 1e-6f, $"{expected} against {actual}");
    }

    // Recentring puts tracking space under the head, turned to its heading but level, at floor height for floor tracking.
    [Fact]
    public void Recentring_PutsTheHeadAtTheCentreFacingForward()
    {
        Quaternion lookingAside = Quaternion.AxisAngle(Float3.UnitY, MathF.PI / 2f) * Quaternion.AxisAngle(Float3.UnitX, -0.5f);
        var head = new Posef
        {
            Position = new Vector3f(1f, 1.7f, 2f),
            Orientation = new Quaternionf(lookingAside.X, lookingAside.Y, lookingAside.Z, lookingAside.W),
        };
        var none = new Posef { Orientation = new Quaternionf(0, 0, 0, 1) };

        Posef floor = OpenXRSession.RecenteredOffset(none, head, keepFloor: true);
        AssertClose(new Float3(1f, 0f, 2f), new Float3(floor.Position.X, floor.Position.Y, floor.Position.Z));

        // The head seen from the new space: above the centre, looking straight down the space's forward.
        Quaternion space = AsNumbers(floor.Orientation);
        Float3 headInSpace = Quaternion.Inverse(space) * (new Float3(1f, 1.7f, 2f) - new Float3(floor.Position.X, floor.Position.Y, floor.Position.Z));
        AssertClose(new Float3(0f, 1.7f, 0f), headInSpace);
        Float3 forward = Quaternion.Inverse(space) * (lookingAside * new Float3(0f, 0f, -1f));
        Assert.True(MathF.Abs(forward.X) < 1e-4f && forward.Z < 0f, $"head looks along {forward}");

        Posef seated = OpenXRSession.RecenteredOffset(none, head, keepFloor: false);
        Assert.Equal(1.7f, seated.Position.Y, 4);
    }

    // A second recentre builds on the first, from where the head is in the already recentred space.
    [Fact]
    public void Recentring_Twice_BuildsOnTheFirst()
    {
        var none = new Posef { Orientation = new Quaternionf(0, 0, 0, 1) };
        Quaternion quarterTurn = Quaternion.AxisAngle(Float3.UnitY, MathF.PI / 2f);
        var head = new Posef { Position = new Vector3f(1f, 1.7f, 2f), Orientation = new Quaternionf(quarterTurn.X, quarterTurn.Y, quarterTurn.Z, quarterTurn.W) };
        Posef first = OpenXRSession.RecenteredOffset(none, head, keepFloor: true);

        var stepped = new Posef { Position = new Vector3f(0.5f, 1.7f, 0f), Orientation = new Quaternionf(0, 0, 0, 1) };
        Posef second = OpenXRSession.RecenteredOffset(first, stepped, keepFloor: true);

        Float3 expected = new Float3(1f, 0f, 2f) + quarterTurn * new Float3(0.5f, 0f, 0f);
        AssertClose(expected, new Float3(second.Position.X, second.Position.Y, second.Position.Z));
        Assert.True(Quaternion.Angle(quarterTurn, AsNumbers(second.Orientation)) < 1e-4f);
    }

    private static XRHandJointPose Tip(Float3 position, float radius = 0.008f)
        => new() { Pose = new XRPose { Position = position, HasPosition = true }, Radius = radius };

    [Fact]
    public void Pinch_GoesFromApartToTouching()
    {
        Assert.Equal(1f, OpenXRInput.Pinch(Tip(Float3.Zero), Tip(new Float3(0.016f, 0f, 0f))), 3);
        Assert.Equal(0f, OpenXRInput.Pinch(Tip(Float3.Zero), Tip(new Float3(0.06f, 0f, 0f))), 3);
        float halfway = OpenXRInput.Pinch(Tip(Float3.Zero), Tip(new Float3(0.0335f, 0f, 0f)));
        Assert.InRange(halfway, 0.4f, 0.6f);
        Assert.Equal(0f, OpenXRInput.Pinch(Tip(Float3.Zero), default));
    }

    [Fact]
    public void WithoutXR_TrackingReadsUntracked()
    {
        Assert.False(XR.IsRunning);

        Assert.False(XRInput.GetTouch(XRHand.Right, XRTouch.Trigger));
        Assert.False(XRInput.GetTouchDown(XRHand.Left, XRTouch.Thumbrest));
        Assert.False(XRInput.IsHandTracked(XRHand.Left));
        Assert.False(XRInput.GetHandJoint(XRHand.Right, XRHandJoint.IndexTip).Pose.HasPosition);
        Assert.Equal(0f, XRInput.GetPinch(XRHand.Right));
        Assert.Equal("", XRInput.GetControllerProfile(XRHand.Left));
        Assert.False(XRInput.IsControllerConnected(XRHand.Right));
        Assert.False(XR.GetPose(XRNode.EyeGaze).IsValid);
        Assert.False(XR.IsHandTrackingSupported);
        Assert.False(XR.IsEyeTrackingSupported);
        Assert.False(XR.TryGetPlayAreaSize(out Float2 size));
        Assert.Equal(Float2.Zero, size);
        XR.Recenter();
    }
}
