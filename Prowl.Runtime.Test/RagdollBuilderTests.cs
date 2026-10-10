using Prowl.Motion;
using Prowl.Runtime;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>The ragdoll generator's layout: which parts it makes, how heavy, and which way the joints bend.</summary>
public class RagdollBuilderTests
{
    // A standing humanoid facing +Z with its arms out along X, with only the bones a ragdoll needs and a neck.
    private static (GameObject Root, Dictionary<HumanBodyBone, Transform> Bones) MakeMinimalCharacter()
    {
        var root = new GameObject("Character");
        var bones = new Dictionary<HumanBodyBone, Transform>();

        Transform Bone(HumanBodyBone bone, Transform parent, Float3 world)
        {
            var go = new GameObject(bone.ToString());
            go.SetParent(parent.GameObject, false);
            go.Transform.Position = world;
            bones[bone] = go.Transform;
            return go.Transform;
        }

        Transform hips = Bone(HumanBodyBone.Hips, root.Transform, new Float3(0f, 1f, 0f));
        Transform chest = Bone(HumanBodyBone.Chest, hips, new Float3(0f, 1.3f, 0f));
        Transform neck = Bone(HumanBodyBone.Neck, chest, new Float3(0f, 1.55f, 0f));
        Bone(HumanBodyBone.Head, neck, new Float3(0f, 1.65f, 0f));
        foreach (float side in new[] { -1f, 1f })
        {
            bool left = side < 0f;
            Transform upperArm = Bone(left ? HumanBodyBone.LeftUpperArm : HumanBodyBone.RightUpperArm, chest, new Float3(0.2f * side, 1.45f, 0f));
            Transform lowerArm = Bone(left ? HumanBodyBone.LeftLowerArm : HumanBodyBone.RightLowerArm, upperArm, new Float3(0.5f * side, 1.45f, 0f));
            Bone(left ? HumanBodyBone.LeftHand : HumanBodyBone.RightHand, lowerArm, new Float3(0.75f * side, 1.45f, 0f));
            Transform upperLeg = Bone(left ? HumanBodyBone.LeftUpperLeg : HumanBodyBone.RightUpperLeg, hips, new Float3(0.1f * side, 0.95f, 0f));
            Transform lowerLeg = Bone(left ? HumanBodyBone.LeftLowerLeg : HumanBodyBone.RightLowerLeg, upperLeg, new Float3(0.1f * side, 0.5f, 0f));
            Bone(left ? HumanBodyBone.LeftFoot : HumanBodyBone.RightFoot, lowerLeg, new Float3(0.1f * side, 0.08f, 0f));
        }
        return (root, bones);
    }

    private static T On<T>(Dictionary<HumanBodyBone, Transform> bones, HumanBodyBone bone) where T : Component
        => bones[bone].GameObject.GetComponent<T>();

    // Hips, chest, neck and head, and two parts per limb: every part this rig has bones for.
    [Fact]
    public void EveryPartGetsABody_AndTheyWeighTheTotalMass()
    {
        (GameObject root, Dictionary<HumanBodyBone, Transform> bones) = MakeMinimalCharacter();
        RagdollBuilder.Build(root.Transform, bones, new RagdollBuilder.Settings { TotalMass = 80f });

        var bodies = root.GetComponentsInChildren<Rigidbody3D>().ToList();
        Assert.Equal(12, bodies.Count);
        Assert.Equal(80f, bodies.Sum(b => b.Mass), 2);
        Assert.Null(On<Rigidbody3D>(bones, HumanBodyBone.LeftHand));
    }

    [Fact]
    public void HandsAndFeet_AddFourMoreParts()
    {
        (GameObject root, Dictionary<HumanBodyBone, Transform> bones) = MakeMinimalCharacter();
        RagdollBuilder.Build(root.Transform, bones, new RagdollBuilder.Settings { HandsAndFeet = true });

        Assert.Equal(16, root.GetComponentsInChildren<Rigidbody3D>().Count());
        Assert.Same(On<Rigidbody3D>(bones, HumanBodyBone.LeftLowerArm), On<BallSocketConstraint>(bones, HumanBodyBone.LeftHand).ConnectedBody);
    }

    [Fact]
    public void ALimbsCapsuleRunsAlongTheBone()
    {
        (GameObject root, Dictionary<HumanBodyBone, Transform> bones) = MakeMinimalCharacter();
        RagdollBuilder.Build(root.Transform, bones, new RagdollBuilder.Settings());

        CapsuleCollider capsule = On<CapsuleCollider>(bones, HumanBodyBone.LeftUpperArm);
        Float3 axis = Quaternion.FromEuler(capsule.Rotation) * Float3.UnitY;
        Assert.True(MathF.Abs(Float3.Dot(axis, new Float3(-1f, 0f, 0f))) > 0.999f);
        Assert.Equal(0.3f, capsule.Height, 3);
        Assert.Equal(-0.15f, capsule.Center.X, 3);
    }

    // Elbows and knees twist along the bone as well as bend, so a joint holding them to one axis would
    // fight an animation that turns the forearm.
    [Fact]
    public void ElbowsAndKneesAllowTheirOwnTwist()
    {
        (GameObject root, Dictionary<HumanBodyBone, Transform> bones) = MakeMinimalCharacter();
        RagdollBuilder.Build(root.Transform, bones, new RagdollBuilder.Settings());

        foreach (HumanBodyBone bone in new[] { HumanBodyBone.LeftLowerArm, HumanBodyBone.RightLowerLeg })
        {
            Assert.NotNull(On<BallSocketConstraint>(bones, bone));
            Assert.True(On<TwistAngleConstraint>(bones, bone).MaxAngle > 0f);
        }
    }

    // Every bone the animation moves between two parts has a part of its own, so no joint holds a gap
    // the animation changes.
    [Fact]
    public void TheNeckIsAPartOfItsOwn_WhenTheRigHasOne()
    {
        (GameObject root, Dictionary<HumanBodyBone, Transform> bones) = MakeMinimalCharacter();
        RagdollBuilder.Build(root.Transform, bones, new RagdollBuilder.Settings());

        Assert.Same(On<Rigidbody3D>(bones, HumanBodyBone.Neck), On<BallSocketConstraint>(bones, HumanBodyBone.Head).ConnectedBody);
    }

    [Fact]
    public void AnExistingRagdoll_IsNoticed()
    {
        (GameObject root, Dictionary<HumanBodyBone, Transform> bones) = MakeMinimalCharacter();
        Assert.False(RagdollBuilder.HasRagdoll(bones));

        RagdollBuilder.Build(root.Transform, bones, new RagdollBuilder.Settings());
        Assert.True(RagdollBuilder.HasRagdoll(bones));
    }

    // The avatar's muscle ranges end where a knee or elbow is straight, so the joint cannot bend backwards.
    [Fact]
    public void WithAnAvatar_KneesAndElbowsStopAtStraight()
    {
        (Prowl.Motion.Skeleton skeleton, GameObject root) = TestHumanoid.Build();
        Avatar avatar = Avatar.CreateAutomatic(skeleton, out _, "Character");
        Dictionary<HumanBodyBone, Transform> bones = TestHumanoid.Bones(root);
        RagdollBuilder.Build(root.Transform, bones, new RagdollBuilder.Settings { Avatar = avatar });

        foreach (HumanBodyBone bone in new[] { HumanBodyBone.LeftLowerLeg, HumanBodyBone.RightLowerLeg, HumanBodyBone.LeftLowerArm, HumanBodyBone.RightLowerArm })
        {
            ConeLimitConstraint cone = On<ConeLimitConstraint>(bones, bone);
            Float3 straight = Float3.Normalize(cone.Transform.TransformDirection(cone.Axis));
            Float3 centre = Float3.Normalize(cone.ConnectedBody!.Transform.TransformDirection(cone.ConnectedAxis!.Value));
            float degrees = Float3.AngleBetween(straight, centre) * Maths.Rad2Deg;
            Assert.Equal(cone.MaxAngle, degrees, 0.5f);
        }
    }
}
