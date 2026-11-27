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

/// <summary>
/// The base of the animators: owns the working <see cref="Pose"/>, advances playback, and pushes the
/// result out through hooks a host engine implements, such as <see cref="ApplyBoneTransform"/>.
/// </summary>
public abstract class AnimatorBase
{
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

    /// <summary>Pushes one bone's local transform onto the engine object bound to it.</summary>
    protected abstract void ApplyBoneTransform(int boneIndex, in Transform3D localTransform);

    /// <summary>Applies the per-update root motion delta to the character (called only when enabled).</summary>
    protected virtual void ApplyRootMotion(in Transform3D delta) { }

    /// <summary>The character root's world transform.</summary>
    protected virtual Transform3D RootWorldTransform => Transform3D.Identity;

    /// <summary>Advances playback and writes the parent-space pose into <see cref="Pose"/>.</summary>
    protected abstract void Evaluate(float scaledDeltaTime, out Transform3D rootMotionDelta);

    /// <summary>Advances the animation by <paramref name="deltaTime"/> seconds and pushes the result to the engine.</summary>
    public void Update(float deltaTime)
    {
        float scaled = deltaTime * Speed;
        Evaluate(float.IsFinite(scaled) ? scaled : 0f, out Transform3D rootMotion);
        RootMotionDelta = rootMotion;

        Pose.CalculateModelSpaceTransforms();

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
}
