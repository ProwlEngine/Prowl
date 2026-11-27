using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>A configurable synthetic humanoid for the humanoid tests. Faces +Z with the left side on -X.</summary>
internal sealed class HumanoidTestRig
{
    public bool Chest = true;
    public bool UpperChest = true;
    public bool Neck = true;
    public bool Shoulders = true;
    public bool Toes = true;
    public bool Fingers;
    public float NeckLeanDegrees;
    public float ArmDownDegrees;
    public float LegScale = 1f;
    public Quaternion ArmatureRotation = Quaternion.Identity;
    public string[] FloatChannels = Array.Empty<string>();

    public static Float3 P(float x, float y, float z) => new(x, y, z);

    public (Skeleton Skeleton, HumanDescription Description) Build()
    {
        var defs = new List<(HumanBodyBone Bone, Float3 Model)>();
        float legTop = 1f * LegScale;
        defs.Add((HumanBodyBone.Hips, P(0f, legTop, 0f)));
        defs.Add((HumanBodyBone.Spine, P(0f, legTop + 0.1f, 0f)));
        if (Chest) defs.Add((HumanBodyBone.Chest, P(0f, legTop + 0.25f, 0f)));
        if (UpperChest) defs.Add((HumanBodyBone.UpperChest, P(0f, legTop + 0.38f, 0f)));

        float neckBase = legTop + 0.52f;
        if (Neck) defs.Add((HumanBodyBone.Neck, P(0f, neckBase, 0f)));
        float lean = NeckLeanDegrees * MathF.PI / 180f;
        defs.Add((HumanBodyBone.Head, P(0f, neckBase + 0.25f * MathF.Cos(lean), 0.25f * MathF.Sin(lean))));

        float armDown = ArmDownDegrees * MathF.PI / 180f;
        foreach (bool left in new[] { true, false })
        {
            float side = left ? -1f : 1f;
            HumanBodyBone B(string name) => Enum.Parse<HumanBodyBone>((left ? "Left" : "Right") + name);
            float shoulderY = legTop + 0.48f;
            if (Shoulders) defs.Add((B("Shoulder"), P(side * 0.04f, shoulderY, 0f)));
            Float3 upper = P(side * 0.17f, shoulderY, 0f);
            Float3 dir = P(side * MathF.Cos(armDown), -MathF.Sin(armDown), 0f);
            Float3 lower = upper + dir * 0.28f;
            Float3 hand = lower + dir * 0.25f;
            defs.Add((B("UpperArm"), upper));
            defs.Add((B("LowerArm"), lower));
            defs.Add((B("Hand"), hand));
            if (Fingers)
            {
                defs.Add((B("IndexProximal"), hand + dir * 0.08f));
                defs.Add((B("IndexIntermediate"), hand + dir * 0.12f));
                defs.Add((B("IndexDistal"), hand + dir * 0.15f));
            }

            defs.Add((B("UpperLeg"), P(side * 0.1f, legTop, 0f)));
            defs.Add((B("LowerLeg"), P(side * 0.1f, legTop * 0.55f, 0f)));
            defs.Add((B("Foot"), P(side * 0.1f, 0.1f * LegScale, 0f)));
            if (Toes) defs.Add((B("Toes"), P(side * 0.1f, 0.02f * LegScale, 0.12f * LegScale)));
        }

        var present = new HashSet<HumanBodyBone>(defs.Select(d => d.Bone));
        var order = defs.OrderBy(d => (int)d.Bone).ToList();
        var index = new Dictionary<HumanBodyBone, int>();
        for (int i = 0; i < order.Count; i++)
            index[order[i].Bone] = i + 1;

        int count = order.Count + 1;
        var ids = new StringID[count];
        var parents = new int[count];
        var local = new Transform3D[count];
        ids[0] = new StringID("Armature");
        parents[0] = Skeleton.InvalidIndex;
        local[0] = new Transform3D(Float3.Zero, ArmatureRotation, Float3.One);

        var description = new HumanDescription();
        for (int i = 0; i < order.Count; i++)
        {
            (HumanBodyBone bone, Float3 model) = order[i];
            HumanBodyBone? parent = HumanTrait.GetParentBone(bone);
            while (parent is not null && !present.Contains(parent.Value))
                parent = HumanTrait.GetParentBone(parent.Value);

            Float3 parentModel = parent is null ? Float3.Zero : order.First(d => d.Bone == parent.Value).Model;
            ids[i + 1] = new StringID(bone.ToString());
            parents[i + 1] = parent is null ? 0 : index[parent.Value];
            local[i + 1] = new Transform3D(model - parentModel, Quaternion.Identity, Float3.One);
            description.SetSkeletonBoneIndex(bone, i + 1);
        }

        var channels = FloatChannels.Select(n => new StringID(n)).ToArray();
        return (new Skeleton(ids, parents, local, -1, channels), description);
    }

    public Avatar BuildAvatar()
    {
        (Skeleton skeleton, HumanDescription description) = Build();
        return AvatarBuilder.BuildHumanoid(skeleton, description);
    }

    public static float AngleDeg(Quaternion a, Quaternion b)
        => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 0f, 1f)) * 180f / MathF.PI;

    public static float AngleDeg(Float3 a, Float3 b)
        => MathF.Acos(Math.Clamp(Float3.Dot(Float3.Normalize(a), Float3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    public static Pose BindPose(Avatar avatar)
    {
        var pose = new Pose(avatar.Skeleton);
        pose.SetToReferencePose();
        return pose;
    }

    public static Pose Retarget(Avatar source, Pose sourcePose, Avatar target)
    {
        var human = new HumanPose();
        Retargeter.RetargetFrom(source, sourcePose, human);
        var result = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, result);
        result.CalculateModelSpaceTransforms();
        return result;
    }

    public static int Index(Avatar avatar, HumanBodyBone bone) => avatar.Humanoid!.GetSkeletonBoneIndex(bone);

    public static Float3 ModelPos(Avatar avatar, Pose pose, HumanBodyBone bone) => pose.GetModelSpaceTransform(Index(avatar, bone)).position;

    public static Quaternion ModelRot(Avatar avatar, Pose pose, HumanBodyBone bone) => pose.GetModelSpaceTransform(Index(avatar, bone)).rotation;

    public static void RotateLocal(Avatar avatar, Pose pose, HumanBodyBone bone, Quaternion modelSpaceDelta)
    {
        int i = Index(avatar, bone);
        int parent = avatar.Skeleton.GetParentBoneIndex(i);
        pose.CalculateModelSpaceTransforms();
        Quaternion parentWorld = parent == Skeleton.InvalidIndex ? Quaternion.Identity : pose.GetModelSpaceTransform(parent).rotation;
        Quaternion world = modelSpaceDelta * pose.GetModelSpaceTransform(i).rotation;
        Transform3D t = pose.GetTransform(i);
        pose.SetTransform(i, new Transform3D(t.position, Quaternion.Normalize(Quaternion.Inverse(parentWorld) * world), t.scale));
        pose.CalculateModelSpaceTransforms();
    }
}
