using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A skeleton independent humanoid pose in muscle space: the body position and rotation, one value per
/// muscle mapping -1..1 onto its range, four IK goals and the look at.
/// </summary>
public sealed class HumanPose
{
    /// <summary>Number of IK goals (feet and hands).</summary>
    public const int GoalCount = 4;

    private readonly float[] _muscles;
    private readonly HumanGoalState[] _goals;

    public HumanPose()
    {
        _muscles = new float[HumanTrait.MuscleCount];
        _goals = new HumanGoalState[GoalCount];
        Reset();
    }

    /// <summary>The centre of mass divided by the avatar scale, so it is about (0, 1, 0) in the T pose.</summary>
    public Float3 BodyPosition { get; set; }

    /// <summary>Rotation of the body frame from its T pose orientation.</summary>
    public Quaternion BodyRotation { get; set; }

    /// <summary>The body position and rotation as one transform (unit scale).</summary>
    public Transform3D RootTransform
    {
        get => new(BodyPosition, BodyRotation, Float3.One);
        set
        {
            BodyPosition = value.position;
            BodyRotation = value.rotation;
        }
    }

    /// <summary>Model space look at target (used when any look at weight is greater than zero).</summary>
    public Float3 LookAtPosition { get; set; }

    /// <summary>Clamp on how far the look may deviate (0 = unclamped, 1 = tight cone).</summary>
    public float LookAtClampWeight { get; set; }

    /// <summary>How much the spine/chest contribute to the look.</summary>
    public float LookAtBodyWeight { get; set; }

    /// <summary>How much the neck/head contribute to the look.</summary>
    public float LookAtHeadWeight { get; set; }

    /// <summary>How much the eyes contribute to the look.</summary>
    public float LookAtEyesWeight { get; set; }

    /// <summary>All muscle values, indexed like <see cref="HumanTrait.GetMuscleName"/>.</summary>
    public Span<float> Muscles => _muscles;

    public float GetMuscle(int muscle) => _muscles[muscle];

    public void SetMuscle(int muscle, float value) => _muscles[muscle] = value;

    /// <summary>The muscle value driving an axis of a bone, or 0 if that axis has no muscle.</summary>
    public float GetMuscle(HumanBodyBone bone, MuscleAxis axis)
    {
        int muscle = HumanTrait.GetMuscleIndex(bone, axis);
        return muscle < 0 ? 0f : _muscles[muscle];
    }

    public HumanGoalState GetGoal(HumanGoal goal) => _goals[(int)goal];

    public void SetGoal(HumanGoal goal, HumanGoalState state) => _goals[(int)goal] = state;

    public void CopyFrom(HumanPose other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Array.Copy(other._muscles, _muscles, _muscles.Length);
        Array.Copy(other._goals, _goals, _goals.Length);
        BodyPosition = other.BodyPosition;
        BodyRotation = other.BodyRotation;
        LookAtPosition = other.LookAtPosition;
        LookAtClampWeight = other.LookAtClampWeight;
        LookAtBodyWeight = other.LookAtBodyWeight;
        LookAtHeadWeight = other.LookAtHeadWeight;
        LookAtEyesWeight = other.LookAtEyesWeight;
    }

    /// <summary>Resets to all zero muscles with the body upright at its T pose height, no goals and no look at.</summary>
    public void Reset()
    {
        Array.Clear(_muscles);
        for (int i = 0; i < _goals.Length; i++)
            _goals[i] = new HumanGoalState { Transform = Transform3D.Identity };
        BodyPosition = new Float3(0f, 1f, 0f);
        BodyRotation = Quaternion.Identity;
        LookAtPosition = default;
        LookAtClampWeight = 0f;
        LookAtBodyWeight = 0f;
        LookAtHeadWeight = 0f;
        LookAtEyesWeight = 0f;
    }
}
