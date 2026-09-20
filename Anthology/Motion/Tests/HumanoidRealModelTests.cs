using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Vector;
using Prowl.Vector.Spatial;
using static Prowl.Motion.Tests.HumanoidTestRig;
using ClayClip = Prowl.Clay.AnimationClip;

namespace Prowl.Motion.Tests;

/// <summary>Muscle space retargeting on the real test models and their clips.</summary>
public class HumanoidRealModelTests
{
    private static readonly HumanBodyBone[] s_limbs =
    {
        HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftLowerArm, HumanBodyBone.RightUpperArm, HumanBodyBone.RightLowerArm,
        HumanBodyBone.LeftUpperLeg, HumanBodyBone.LeftLowerLeg, HumanBodyBone.RightUpperLeg, HumanBodyBone.RightLowerLeg,
    };

    private static Pose Sample(Skeleton skeleton, ClayClip clip, float time)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose(calculateModelSpace: false);
        foreach (AnimationBinding binding in clip.Bindings)
        {
            if (!skeleton.IsValidBoneIndex(binding.NodeIndex))
                continue;
            Transform3D local = pose.GetTransform(binding.NodeIndex);
            if (binding.Property == AnimatedProperty.Rotation)
                pose.SetTransform(binding.NodeIndex, new Transform3D(local.position, binding.Curve.EvaluateQuaternion(time), local.scale));
            else if (binding.Property == AnimatedProperty.Position)
                pose.SetTransform(binding.NodeIndex, new Transform3D(binding.Curve.EvaluateFloat3(time), local.rotation, local.scale));
        }
        pose.CalculateModelSpaceTransforms();
        return pose;
    }

    [ModelTheory]
    [InlineData("Rumba Dancing.fbx")]
    [InlineData("Head Spinning.fbx")]
    public void ClipRoundTrip_KeepsJointsAndEndEffectors(string file)
    {
        Model model = ModelImporter.Load(Path.Combine(Path.GetDirectoryName(TestAssets.RumbaDancing)!, file));
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        ClayClip clip = model.AnimationClips[0];

        HumanBodyBone[] ends = { HumanBodyBone.LeftHand, HumanBodyBone.RightHand, HumanBodyBone.LeftFoot, HumanBodyBone.RightFoot, HumanBodyBone.Head };
        for (int f = 0; f < 20; f++)
        {
            Pose source = Sample(skeleton, clip, clip.Duration * f / 20f);
            Pose result = Retarget(avatar, source, avatar);

            foreach (HumanBodyBone bone in Enum.GetValues<HumanBodyBone>().Where(b => b < HumanBodyBone.LeftThumbProximal && avatar.Humanoid!.HasBone(b)))
            {
                float miss = Float3.Distance(ModelPos(avatar, source, bone), ModelPos(avatar, result, bone));
                Assert.True(miss < 0.04f * avatar.Humanoid!.Scale, $"frame {f}: {bone} moved {miss:N3}");
            }
            foreach (HumanBodyBone bone in ends)
            {
                float error = AngleDeg(ModelRot(avatar, source, bone), ModelRot(avatar, result, bone));
                Assert.True(error < 6f, $"frame {f}: {bone} turned {error:N1} degrees");
            }
        }
    }

    // A limb direction in the rig's current body frame (X right, Y up, Z forward).
    private static Float3 BodyDirection(Avatar avatar, Pose pose, HumanBodyBone bone)
    {
        HumanoidRig rig = avatar.Humanoid!;
        Float3 Reflect(Float3 v) => v;
        Quaternion ReflectQ(Quaternion q) => q;

        HumanBodyBone child = bone switch
        {
            HumanBodyBone.LeftUpperArm => HumanBodyBone.LeftLowerArm,
            HumanBodyBone.LeftLowerArm => HumanBodyBone.LeftHand,
            HumanBodyBone.RightUpperArm => HumanBodyBone.RightLowerArm,
            HumanBodyBone.RightLowerArm => HumanBodyBone.RightHand,
            HumanBodyBone.LeftUpperLeg => HumanBodyBone.LeftLowerLeg,
            HumanBodyBone.LeftLowerLeg => HumanBodyBone.LeftFoot,
            HumanBodyBone.RightUpperLeg => HumanBodyBone.RightLowerLeg,
            _ => HumanBodyBone.RightFoot,
        };
        int hips = Index(avatar, HumanBodyBone.Hips);
        Quaternion body = ReflectQ(pose.GetModelSpaceTransform(hips).rotation) * Quaternion.Inverse(ReflectQ(avatar.Skeleton.GetBoneModelSpaceTransform(hips).rotation)) * rig.BodyBindRotation;
        return Float3.Normalize(Quaternion.Inverse(body) * Reflect(ModelPos(avatar, pose, child) - ModelPos(avatar, pose, bone)));
    }

    [ModelFact]
    public void MixamoClip_OnUnrealRig_KeepsLimbDirections()
    {
        Model rumba = ModelImporter.Load(TestAssets.RumbaDancing);
        Skeleton sourceSkeleton = ClayModelLoader.BuildSkeleton(rumba);
        Avatar source = AvatarBuilder.BuildAutomatic(sourceSkeleton);
        Avatar target = AvatarBuilder.BuildAutomatic(ClayModelLoader.BuildSkeleton(ModelImporter.Load(TestAssets.Daredevil)));
        Assert.True(source.IsHuman && target.IsHuman);
        ClayClip clip = rumba.AnimationClips[0];

        for (int f = 0; f < 10; f++)
        {
            Pose pose = Sample(sourceSkeleton, clip, clip.Duration * f / 10f);
            var human = new HumanPose();
            Retargeter.RetargetFrom(source, pose, human);
            var result = new Pose(target.Skeleton);
            Retargeter.RetargetTo(target, human, result);
            result.CalculateModelSpaceTransforms();

            foreach (HumanBodyBone bone in s_limbs)
            {
                float error = AngleDeg(BodyDirection(source, pose, bone), BodyDirection(target, result, bone));
                Assert.True(error < 8f, $"frame {f}: {bone} points {error:N1} degrees away from the source");
            }
        }
    }
}
