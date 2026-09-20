using System.Runtime.CompilerServices;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Mirrors humanoid poses left to right in muscle space. Left and right muscles and goals swap, the muscles whose direction reverses under a
/// reflection change sign, and the body position and rotation are reflected across the sagittal plane of
/// the bind body frame, so the result does not depend on how the current pose is twisted.
/// </summary>
public static class PoseMirror
{
    [ThreadStatic] private static HumanPose? s_source;
    [ThreadStatic] private static HumanPose? s_mirrored;
    [ThreadStatic] private static ConditionalWeakTable<Skeleton, Pose>? s_rebuilt;
    [ThreadStatic] private static Transform3D[]? s_sourceLocal;
    [ThreadStatic] private static Transform3D[]? s_model;

    /// <summary>Writes the mirror of <paramref name="source"/> into <paramref name="result"/>. They may be the same pose.</summary>
    public static void Mirror(HumanPose source, HumanPose result)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);

        Span<float> muscles = stackalloc float[HumanTrait.MuscleCount];
        for (int i = 0; i < muscles.Length; i++)
            muscles[HumanTrait.GetMirrorMuscle(i)] = HumanTrait.MuscleFlipsWhenMirrored(i) ? -source.GetMuscle(i) : source.GetMuscle(i);

        Span<HumanGoalState> goals = stackalloc HumanGoalState[HumanPose.GoalCount];
        for (int g = 0; g < HumanPose.GoalCount; g++)
        {
            HumanGoalState state = source.GetGoal((HumanGoal)g);
            state.Transform = new Transform3D(Reflect(state.Transform.position), ReflectFrame(state.Transform.rotation, (HumanGoal)g), Float3.One);
            state.Pole = Reflect(state.Pole);
            goals[(int)MirrorGoal((HumanGoal)g)] = state;
        }

        result.CopyFrom(source);
        for (int i = 0; i < muscles.Length; i++)
            result.SetMuscle(i, muscles[i]);
        for (int g = 0; g < HumanPose.GoalCount; g++)
            result.SetGoal((HumanGoal)g, goals[g]);
        result.BodyPosition = Reflect(source.BodyPosition);
        result.BodyRotation = ReflectRotation(source.BodyRotation);
    }

    /// <summary>
    /// Writes the mirror of <paramref name="source"/> (a pose of the humanoid avatar) into
    /// <paramref name="result"/>. Humanoid bones are rebuilt from the mirrored muscle pose. Other bones
    /// take the reflected local transform of their counterpart on the other side (or their own on the
    /// midline), and keep the source pose when they have no counterpart. The look at is not mirrored.
    /// </summary>
    public static void Apply(Avatar avatar, Pose source, Pose result)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);
        if (!avatar.IsHuman)
            throw new InvalidOperationException("Mirroring requires a humanoid avatar.");

        HumanoidRig rig = avatar.Humanoid!;
        Skeleton skeleton = avatar.Skeleton;
        HumanPose human = s_source ??= new HumanPose();
        HumanPose mirrored = s_mirrored ??= new HumanPose();

        Retargeter.RetargetFrom(avatar, source, human);
        Quaternion toBody = rig.FacingToBody;
        human.BodyPosition = Quaternion.Inverse(toBody) * human.BodyPosition;
        human.BodyRotation = Quaternion.Normalize(Quaternion.Inverse(toBody) * human.BodyRotation * toBody);
        Mirror(human, mirrored);
        mirrored.BodyPosition = toBody * mirrored.BodyPosition;
        mirrored.BodyRotation = Quaternion.Normalize(toBody * mirrored.BodyRotation * Quaternion.Inverse(toBody));

        Pose rebuilt = RebuiltPose(skeleton);
        Retargeter.RetargetTo(avatar, mirrored, rebuilt);
        rebuilt.CalculateModelSpaceTransforms();

        int count = skeleton.BoneCount;
        Transform3D[] sourceLocal = Buffer(ref s_sourceLocal, count);
        Transform3D[] model = Buffer(ref s_model, count);
        for (int i = 0; i < count; i++)
            sourceLocal[i] = source.GetTransform(i);

        int[] parents = skeleton.SanitizedParentIndices;
        int hipsIndex = rig.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        Float3 mirroredHips = ReflectVector(source.GetModelSpaceTransform(hipsIndex).position, rig.MirrorNormal);
        result.CopyFrom(source);
        foreach (int index in skeleton.EvaluationOrder)
        {
            int parent = parents[index];
            Transform3D parentModel = parent == Skeleton.InvalidIndex ? Transform3D.Identity : model[parent];
            Transform3D local;
            if (index == hipsIndex)
            {
                Quaternion rotation = rebuilt.GetModelSpaceTransform(index).rotation;
                local = new Transform3D(parentModel.InverseTransformPoint(mirroredHips), Quaternion.Normalize(Quaternion.Inverse(parentModel.rotation) * rotation), rebuilt.GetTransform(index).scale);
            }
            else if (rig.TryGetHumanBone(index, out _))
            {
                local = rebuilt.GetTransform(index);
            }
            else
            {
                int counterpart = rig.GetMirrorBoneIndex(index);
                local = counterpart == Skeleton.InvalidIndex ? sourceLocal[index] : MirrorLocal(rig, counterpart, index, sourceLocal[counterpart]);
            }

            model[index] = TransformOps.Combine(parentModel, local);
            result.SetTransform(index, local);
        }

        Retargeter.RepositionDisconnectedBones(avatar, result);
    }

    // Reflects the local transform of bone from onto its counterpart to, measured against both bind poses.
    private static Transform3D MirrorLocal(HumanoidRig rig, int from, int to, Transform3D local)
    {
        Skeleton skeleton = rig.Skeleton;
        int[] parents = skeleton.SanitizedParentIndices;
        Quaternion fromParent = BindRotation(skeleton, parents[from]);
        Quaternion toParent = BindRotation(skeleton, parents[to]);
        Float3 normal = rig.MirrorNormal;

        Quaternion delta = fromParent * local.rotation * Quaternion.Inverse(skeleton.GetBoneModelSpaceTransform(from).rotation);
        Quaternion rotation = Quaternion.Inverse(toParent) * ReflectRotation(delta, normal) * skeleton.GetBoneModelSpaceTransform(to).rotation;
        Float3 position = Quaternion.Inverse(toParent) * ReflectVector(fromParent * local.position, normal);
        return new Transform3D(position, Quaternion.Normalize(rotation), local.scale);
    }

    /// <summary>
    /// The skeleton bone each bone mirrors onto: the opposite humanoid bone for mapped bones, and for other
    /// bones the child of their parent's counterpart whose bind position is the reflection of theirs (itself
    /// on the midline). <see cref="Skeleton.InvalidIndex"/> where there is no counterpart.
    /// </summary>
    internal static int[] BuildMirrorBones(HumanoidRig rig)
    {
        Skeleton skeleton = rig.Skeleton;
        int[] parents = skeleton.SanitizedParentIndices;
        var mirror = new int[skeleton.BoneCount];
        Array.Fill(mirror, Skeleton.InvalidIndex);

        foreach (HumanBodyBone bone in HumanTrait.AllBones)
        {
            HumanBodyBone opposite = HumanTrait.GetMirrorBone(bone);
            if (rig.HasBone(bone) && rig.HasBone(opposite))
                mirror[rig.GetSkeletonBoneIndex(bone)] = rig.GetSkeletonBoneIndex(opposite);
        }

        Float3 left = skeleton.GetBoneModelSpaceTransform(rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg)).position;
        Float3 right = skeleton.GetBoneModelSpaceTransform(rig.GetSkeletonBoneIndex(HumanBodyBone.RightUpperLeg)).position;
        Float3 center = (left + right) * 0.5f;
        float tolerance = 0.02f * rig.Scale;

        foreach (int index in skeleton.EvaluationOrder)
        {
            if (rig.TryGetHumanBone(index, out _))
                continue;
            int parent = parents[index];
            int parentMirror = parent == Skeleton.InvalidIndex ? Skeleton.InvalidIndex : mirror[parent];
            if (parent != Skeleton.InvalidIndex && parentMirror == Skeleton.InvalidIndex)
                continue;

            int match = NearestReflection(rig, index, parentMirror, center, tolerance);
            if (match != Skeleton.InvalidIndex && NearestReflection(rig, match, parent, center, tolerance) == index)
                mirror[index] = match;
        }
        return mirror;
    }

    // The unmapped child of parent (a root when parent is invalid) nearest to the reflection of the bone's bind position.
    private static int NearestReflection(HumanoidRig rig, int bone, int parent, Float3 center, float tolerance)
    {
        Skeleton skeleton = rig.Skeleton;
        int[] parents = skeleton.SanitizedParentIndices;
        Float3 reflected = center + ReflectVector(skeleton.GetBoneModelSpaceTransform(bone).position - center, rig.MirrorNormal);

        int best = Skeleton.InvalidIndex;
        float bestDistance = tolerance;
        for (int i = 0; i < skeleton.BoneCount; i++)
        {
            if (parents[i] != parent || rig.TryGetHumanBone(i, out _))
                continue;
            float distance = Float3.Distance(skeleton.GetBoneModelSpaceTransform(i).position, reflected);
            if (distance <= bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static Quaternion BindRotation(Skeleton skeleton, int bone)
        => bone == Skeleton.InvalidIndex ? Quaternion.Identity : skeleton.GetBoneModelSpaceTransform(bone).rotation;

    private static Float3 ReflectVector(Float3 v, Float3 normal) => v - normal * (2f * Float3.Dot(v, normal));

    // A rotation seen in a mirror: its axis reflected and its angle reversed.
    private static Quaternion ReflectRotation(Quaternion q, Float3 normal)
    {
        Float3 axis = ReflectVector(new Float3(q.X, q.Y, q.Z), normal);
        return new Quaternion(-axis.X, -axis.Y, -axis.Z, q.W);
    }

    private static Transform3D[] Buffer(ref Transform3D[]? buffer, int count)
        => buffer is { } existing && existing.Length >= count ? existing : buffer = new Transform3D[count];

    /// <summary>
    /// Mirrors a model space root motion delta of the humanoid avatar across the sagittal plane of its bind
    /// body frame, the same plane <see cref="Apply"/> mirrors the pose across. The translation and the turn
    /// are reflected, so a step and turn to the left becomes a step and turn to the right. The scale is kept.
    /// </summary>
    public static Transform3D MirrorRootMotion(Avatar avatar, Transform3D delta)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        if (!avatar.IsHuman)
            throw new InvalidOperationException("Mirroring requires a humanoid avatar.");

        HumanoidRig rig = avatar.Humanoid!;
        Float3 normal = rig.MirrorNormal;
        Float3 position = delta.position - normal * (2f * Float3.Dot(delta.position, normal));

        Quaternion q = delta.rotation;
        var axis = new Float3(q.X, q.Y, q.Z);
        Float3 reflectedAxis = normal * (2f * Float3.Dot(axis, normal)) - axis;
        return new Transform3D(position, new Quaternion(reflectedAxis.X, reflectedAxis.Y, reflectedAxis.Z, q.W), delta.scale);
    }

    private static Pose RebuiltPose(Skeleton skeleton)
    {
        ConditionalWeakTable<Skeleton, Pose> cache = s_rebuilt ??= new ConditionalWeakTable<Skeleton, Pose>();
        if (!cache.TryGetValue(skeleton, out Pose? pose))
        {
            pose = new Pose(skeleton);
            cache.Add(skeleton, pose);
        }
        return pose;
    }

    private static HumanGoal MirrorGoal(HumanGoal goal) => goal switch
    {
        HumanGoal.LeftFoot => HumanGoal.RightFoot,
        HumanGoal.RightFoot => HumanGoal.LeftFoot,
        HumanGoal.LeftHand => HumanGoal.RightHand,
        _ => HumanGoal.LeftHand,
    };

    // Reflection across the body's sagittal plane (body X is lateral).
    private static Float3 Reflect(Float3 v) => new(-v.X, v.Y, v.Z);

    private static Quaternion ReflectRotation(Quaternion q) => new(q.X, -q.Y, -q.Z, q.W);

    // A reflected axis frame points its X axis the wrong way down the opposite limb. A hand frame is set
    // right by a half turn about Z, a foot frame by a half turn about Y.
    private static Quaternion ReflectFrame(Quaternion q, HumanGoal goal)
    {
        Quaternion halfTurn = goal is HumanGoal.LeftFoot or HumanGoal.RightFoot ? new Quaternion(0f, 1f, 0f, 0f) : new Quaternion(0f, 0f, 1f, 0f);
        return Quaternion.Normalize(ReflectRotation(q) * halfTurn);
    }
}
