// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Motion;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Xunit;

using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime.Test;

/// <summary>The Animator: binding a rig to a hierarchy, playing clips, and moving by root motion.</summary>
public class AnimatorTests : RuntimeTestBase
{
    private static readonly StringID RootId = new("Rig");
    private static readonly StringID HipsId = new("Hips");
    private static readonly StringID SpineId = new("Spine");

    // Rig -> Hips -> Spine, each a unit above the last.
    private static MotionSkeleton BuildSkeleton() => new(
        new[] { RootId, HipsId, SpineId },
        new[] { MotionSkeleton.InvalidIndex, 0, 1 },
        new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
        });

    private static GameObject BuildHierarchy(GameObject root)
    {
        root.Name = "Rig";
        var hips = new GameObject("Hips");
        var spine = new GameObject("Spine");
        hips.SetParent(root, false);
        spine.SetParent(hips, false);
        return root;
    }

    // A clip that slides the spine from y=1 to y=1+distance over a second.
    private static AnimationClip SlideClip(MotionSkeleton skeleton, Avatar avatar, float distance = 2f, Transform3D? rootMotionEnd = null)
    {
        var first = new Pose(skeleton);
        first.SetToReferencePose();
        var last = new Pose(skeleton);
        last.SetToReferencePose();
        last.SetTransform(2, new Transform3D(new Float3(0f, 1f + distance, 0f), Quaternion.Identity, Float3.One));

        RootMotion? rootMotion = rootMotionEnd is { } end
            ? new RootMotion(new[] { Transform3D.Identity, end }, 1f)
            : null;

        var clip = new Motion.AnimationClip(skeleton, new[] { first, last }, 1f, false, rootMotion);
        return AnimationClip.FromSkeletal(clip, avatar, "Slide");
    }

    private (Scene Scene, Animator Animator, GameObject Root) Setup(float distance = 2f, Transform3D? rootMotionEnd = null)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        Scene scene = CreateScene();
        GameObject root = BuildHierarchy(CreateGameObject("Rig"));
        scene.Add(root);

        var animator = root.AddComponent<Animator>();
        animator.Avatar = avatar;
        animator.Clips = new List<AnimationClip> { SlideClip(skeleton, avatar, distance, rootMotionEnd) };

        scene.Enable();
        return (scene, animator, root);
    }

    private static Transform Spine(GameObject root) => root.Children[0].Children[0].Transform;

    [Fact]
    public void ItBindsTheRigToTheHierarchy()
    {
        (Scene scene, Animator animator, _) = Setup();

        Update(scene);

        Assert.True(animator.IsBound);
        Assert.NotNull(animator.Pose);
    }

    // An object named like a bone but outside the rig's chain, found first in the hierarchy, is not the bone.
    [Fact]
    public void ABoneIsFoundUnderItsParent_NotByAnObjectSharingItsName()
    {
        (Scene scene, Animator animator, GameObject root) = Setup();
        Transform spine = Spine(root);
        var props = new GameObject("Props");
        var decoy = new GameObject("Spine");
        props.SetParent(root, false);
        decoy.SetParent(props, false);
        props.SetSiblingIndex(0);
        animator.Rebind();
        animator.Speed = 0f;

        Update(scene);

        Assert.Equal(1.0, spine.LocalPosition.Y, 2);
        Assert.Equal(0.0, decoy.Transform.LocalPosition.Y, 3);
    }

    [Fact]
    public void ItPlaysTheFirstClipAndDrivesTheBones()
    {
        (Scene scene, Animator animator, GameObject root) = Setup();
        animator.Speed = 0f; // hold at the start so the assertion is about binding, not timing

        Update(scene);

        Assert.NotNull(animator.CurrentClip);
        Assert.Equal(1.0, Spine(root).LocalPosition.Y, 2);
    }

    // Half a second into a one second clip is half way along it.
    [Fact]
    public void ItAdvancesTheClipOverTime()
    {
        (Scene scene, _, GameObject root) = Setup();

        Update(scene, 31); // 1/60 per frame

        Assert.InRange(Spine(root).LocalPosition.Y, 1.8, 2.2);
    }

    // The animator's own Transform is where the character stands, so the pose must not move it.
    [Fact]
    public void ItLeavesItsOwnTransformAlone()
    {
        (Scene scene, _, GameObject root) = Setup();
        root.Transform.Position = new Float3(5f, 0f, 0f);

        Update(scene, 10);

        Assert.Equal(5.0, root.Transform.Position.X, 3);
    }

    [Fact]
    public void RootMotionMovesTheGameObject()
    {
        Transform3D end = new(new Float3(0f, 0f, 4f), Quaternion.Identity, Float3.One);
        (Scene scene, Animator animator, GameObject root) = Setup(rootMotionEnd: end);
        animator.ApplyRootMotion = true;

        Update(scene, 61);

        Assert.InRange(root.Transform.Position.Z, 3.5, 4.5);
    }

    // Root motion is in the character's own units, so a scaled parent scales how far it goes.
    [Fact]
    public void RootMotionUnderAScaledParent_TravelsTheScaledDistance()
    {
        Transform3D end = new(new Float3(0f, 0f, 4f), Quaternion.Identity, Float3.One);
        (Scene scene, Animator animator, GameObject root) = Setup(rootMotionEnd: end);
        GameObject parent = CreateGameObject("Scaled");
        parent.Transform.LocalScale = new Float3(2f, 2f, 2f);
        scene.Add(parent);
        root.SetParent(parent, false);
        animator.ApplyRootMotion = true;

        Update(scene, 61);

        Assert.InRange(root.Transform.Position.Z, 7f, 9f);
    }

    [Fact]
    public void RootMotionStaysPutWhenItIsTurnedOff()
    {
        Transform3D end = new(new Float3(0f, 0f, 4f), Quaternion.Identity, Float3.One);
        (Scene scene, _, GameObject root) = Setup(rootMotionEnd: end);

        Update(scene, 61);

        Assert.Equal(0.0, root.Transform.Position.Z, 3);
    }

    [Fact]
    public void PlayByNameFindsAClipInTheList()
    {
        (Scene scene, Animator animator, GameObject root) = Setup();
        Update(scene);

        animator.Play("Slide");
        animator.Speed = 0f;
        Update(scene);

        Assert.Equal("Slide", animator.CurrentClip!.Name);
        Assert.Equal(1.0, Spine(root).LocalPosition.Y, 2);
    }

    [Fact]
    public void ADisabledAnimatorStopsBeingDriven()
    {
        (Scene scene, Animator animator, GameObject root) = Setup();
        Update(scene, 31);
        double held = Spine(root).LocalPosition.Y;

        animator.Enabled = false;
        Update(scene, 31);

        Assert.Equal(held, Spine(root).LocalPosition.Y, 3);
    }

    // Animation writes the pose before LateUpdate, so a component overriding a bone there wins.
    [Fact]
    public void LateUpdateSeesThePoseAlreadyWritten()
    {
        (Scene scene, _, GameObject root) = Setup();
        var observer = root.AddComponent<SpineObserver>();
        observer.Spine = Spine(root);

        Update(scene, 31);

        Assert.InRange(observer.SeenY, 1.8, 2.2);
    }

    private sealed class SpineObserver : MonoBehaviour
    {
        public Transform? Spine;
        public double SeenY;

        public override void LateUpdate()
        {
            if (Spine != null) SeenY = Spine.LocalPosition.Y;
        }
    }
}
