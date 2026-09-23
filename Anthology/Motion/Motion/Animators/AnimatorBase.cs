using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A way for a graph to ask the world where the ground is, in world space. The engine implements it;
/// Motion only calls it.
/// </summary>
public interface IGroundProbe
{
    /// <summary>Casts a ray and reports what it lands on, or false when it hits nothing.</summary>
    bool Raycast(Float3 worldOrigin, Float3 worldDirection, float maxDistance, out Float3 worldPoint, out Float3 worldNormal);
}

/// <summary> It owns the working <see cref="Pose"/>, advances
/// playback, optionally solves humanoid foot IK against the engine's collision world, and pushes the
/// result out to engine objects.
///
/// It is engine-agnostic: a host engine subclasses this (or <see cref="SimpleAnimator"/> /
/// <see cref="GraphAnimator"/>) and implements the small set of hooks that bind to its own transform
/// and physics systems. In an engine where each skeleton bone maps to a child Transform,
/// <see cref="ApplyBoneTransform"/> writes the bone's local (parent-space) TRS onto that Transform and
/// the engine's hierarchy produces world space; <see cref="RaycastGround"/> wraps the engine's
/// world-space ray cast for foot grounding.
/// </summary>
public abstract class AnimatorBase
{
    private static readonly Float3 Up = new(0f, 1f, 0f);
    private static readonly Float3 Down = new(0f, -1f, 0f);

    protected AnimatorBase(Skeleton skeleton, Avatar? avatar)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        Skeleton = skeleton;
        Avatar = avatar;
        Pose = new Pose(skeleton);
        Pose.SetToReferencePose();
    }

    public Skeleton Skeleton { get; }
    public Avatar? Avatar { get; }

    /// <summary>The current pose. Model-space transforms are valid after <see cref="Update"/>.</summary>
    public Pose Pose { get; }

    /// <summary>Playback speed multiplier applied to the delta time each update.</summary>
    public float Speed { get; set; } = 1f;

    /// <summary>The root motion produced by the most recent <see cref="Update"/> (character space).</summary>
    public Transform3D RootMotionDelta { get; private set; } = Transform3D.Identity;

    /// <summary>When true, applies <see cref="RootMotionDelta"/> to the engine via <see cref="ApplyRootMotion"/>.</summary>
    public bool ApplyRootMotionToEngine { get; set; }

    // ---- Foot IK configuration (humanoid only) ----------------------------------------------

    /// <summary>Enables humanoid foot grounding via the engine's ground ray cast.</summary>
    public bool FootIkEnabled { get; set; }

    /// <summary>Blend weight for foot grounding (0 = off, 1 = fully planted).</summary>
    public float FootIkWeight { get; set; } = 1f;

    /// <summary>How far above the foot to start the ground ray (world units).</summary>
    public float FootRayStartHeight { get; set; } = 0.5f;

    /// <summary>How far below the foot the ground ray reaches (world units).</summary>
    public float FootRayLength { get; set; } = 1.5f;

    // ---- Engine hooks (implemented by the host) ---------------------------------------------

    /// <summary>
    /// Pushes a single bone's local (parent-space) transform onto the engine object bound to that bone.
    /// With bone Transforms, this sets the bone's local position, rotation and scale.
    /// </summary>
    protected abstract void ApplyBoneTransform(int boneIndex, in Transform3D localTransform);

    /// <summary>Applies the per-update root motion delta to the character (called only when enabled).</summary>
    protected virtual void ApplyRootMotion(in Transform3D delta) { }

    /// <summary>The character root's world transform, used to convert foot positions for ground ray casts.</summary>
    protected virtual Transform3D RootWorldTransform => Transform3D.Identity;

    /// <summary>
    /// Casts a ray against the engine's collision world. Return false when nothing is hit. Used for foot
    /// grounding; the default never hits, which disables grounding until a host overrides it.
    /// </summary>
    protected virtual bool RaycastGround(Float3 worldOrigin, Float3 worldDirection, float maxDistance, out Float3 worldHitPoint, out Float3 worldHitNormal)
    {
        worldHitPoint = default;
        worldHitNormal = Up;
        return false;
    }

    // ---- Pose production (implemented by the concrete animator) ------------------------------

    /// <summary>Advances playback and writes the parent-space pose into <see cref="Pose"/>.</summary>
    protected abstract void Evaluate(float scaledDeltaTime, out Transform3D rootMotionDelta);

    // ---- Driving loop -----------------------------------------------------------------------

    /// <summary>Advances the animation by <paramref name="deltaTime"/> seconds and pushes the result to the engine.</summary>
    public void Update(float deltaTime)
    {
        float scaled = deltaTime * Speed;
        Evaluate(float.IsFinite(scaled) ? scaled : 0f, out Transform3D rootMotion);
        RootMotionDelta = rootMotion;

        Pose.CalculateModelSpaceTransforms();

        if (FootIkEnabled && FootIkWeight > 0f && Avatar?.Humanoid is HumanoidRig rig)
            SolveFootIk(rig);

        if (ApplyRootMotionToEngine)
            ApplyRootMotion(rootMotion);

        PushPose();
        AfterPosePushed();
    }

    /// <summary>
    /// Pushes one float channel (a blend shape weight, say) to the engine object bound to it. The
    /// default does nothing, so an engine without float channels ignores them.
    /// </summary>
    protected virtual void ApplyFloatChannel(int channelIndex, float value) { }

    /// <summary>Called after the primary pose is pushed each update (used to push secondary poses).</summary>
    protected virtual void AfterPosePushed() { }

    private void PushPose()
    {
        for (int i = 0; i < Skeleton.BoneCount; i++)
            ApplyBoneTransform(i, Pose.GetTransform(i));

        for (int i = 0; i < Pose.FloatChannelCount; i++)
            ApplyFloatChannel(i, Pose.GetFloat(i));
    }

    private void SolveFootIk(HumanoidRig rig)
    {
        GroundFoot(rig, HumanGoal.LeftFoot, HumanBodyBone.LeftFoot);
        GroundFoot(rig, HumanGoal.RightFoot, HumanBodyBone.RightFoot);
    }

    private void GroundFoot(HumanoidRig rig, HumanGoal goal, HumanBodyBone footBone)
    {
        if (!rig.HasBone(footBone))
            return;

        int foot = rig.GetSkeletonBoneIndex(footBone);
        Float3 footModel = Pose.GetModelSpaceTransform(foot).position;

        Transform3D toWorld = RootWorldTransform;
        Float4x4 toWorldMatrix = toWorld.ToMatrix();
        Float3 footWorld = Float4x4.TransformPoint(footModel, toWorldMatrix);

        Float3 origin = new(footWorld.X, footWorld.Y + FootRayStartHeight, footWorld.Z);
        float maxDistance = FootRayStartHeight + FootRayLength;
        if (!RaycastGround(origin, Down, maxDistance, out Float3 hitWorld, out Float3 hitNormal))
            return;

        Transform3D toModel = TransformOps.Inverse(toWorld);
        Float3 hitModel = Float4x4.TransformPoint(hitWorld, toModel.ToMatrix());
        FootGrounding.Ground(Pose, rig, goal, hitModel, toModel.rotation * hitNormal, FootIkWeight);
        Pose.CalculateModelSpaceTransforms();
    }
}
