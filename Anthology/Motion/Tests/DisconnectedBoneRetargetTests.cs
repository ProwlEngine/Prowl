using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>
/// Some rigs parent the neck/head off the control root instead of the spine (seen in the Fortnite
/// "emmy" export). Such a head would not follow the body when the hips bob, stretching the neck. The
/// retargeter must re-seat those disconnected bones so they ride along with their humanoid parent.
/// </summary>
public class DisconnectedBoneRetargetTests
{
    private static Skeleton NeckUnderRootHumanoid(out int headIndex)
    {
        var names = new[]
        {
            "root", "pelvis", "spine_01",
            "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l",
            "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r",
            "thigh_l", "calf_l", "foot_l",
            "thigh_r", "calf_r", "foot_r",
            "neck_01", "head",
        };
        var parents = new[]
        {
            -1, 0, 1,
            2, 3, 4, 5,
            2, 7, 8, 9,
            1, 11, 12,
            1, 14, 15,
            0, 17, // neck under root (0), head under neck (17)
        };
        // Stack each bone 0.2 above its parent so model-space Y is meaningful.
        var ids = new StringID[names.Length];
        var local = new Transform3D[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            ids[i] = new StringID(names[i]);
            local[i] = new Transform3D(new Float3(0f, 0.2f, 0f), Quaternion.Identity, Float3.One);
        }
        headIndex = 18;
        return new Skeleton(ids, parents, local);
    }

    [Fact]
    public void DisconnectedHead_FollowsTheBodyWhenHipsMove()
    {
        Skeleton skeleton = NeckUnderRootHumanoid(out int head);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        Assert.True(avatar.IsHuman);

        int hips = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        float bindHeadY = skeleton.GetBoneModelSpaceTransform(head).position.Y;

        // Source pose: drop the hips by 0.5 (a "bob"). Everything under the hips moves with it.
        var source = new Pose(skeleton);
        source.SetToReferencePose();
        Transform3D hipsLocal = source.GetTransform(hips);
        source.SetTransform(hips, new Transform3D(new Float3(hipsLocal.position.X, hipsLocal.position.Y - 0.5f, hipsLocal.position.Z), hipsLocal.rotation, hipsLocal.scale));
        source.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(skeleton);
        Retargeter.RetargetTo(avatar, human, result);
        result.CalculateModelSpaceTransforms();

        // The head should have dropped with the body, not stayed locked at its bind height.
        float resultHeadY = result.GetModelSpaceTransform(head).position.Y;
        Assert.True(resultHeadY < bindHeadY - 0.3f, $"head did not follow the body: bind={bindHeadY}, result={resultHeadY}");
    }
}
