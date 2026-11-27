using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>The four humanoid IK goals.</summary>
public enum HumanGoal : byte
{
    LeftFoot,
    RightFoot,
    LeftHand,
    RightHand
}

/// <summary>
/// A humanoid IK goal: the desired end effector transform plus how strongly position and rotation should
/// be enforced. In a <see cref="HumanPose"/> it is expressed in the current body frame, so it is rig independent.
/// </summary>
public struct HumanGoalState
{
    /// <summary>
    /// The goal transform. Its position is the offset from the midpoint of the hip joints divided by the
    /// avatar scale, its rotation the hand or foot's anatomical frame, both in the body frame.
    /// </summary>
    public Transform3D Transform;

    /// <summary>How strongly to drive the end effector to the goal position (0 = ignore, 1 = pin).</summary>
    public float PositionWeight;

    /// <summary>How strongly to drive the end effector to the goal rotation (0 = ignore, 1 = pin).</summary>
    public float RotationWeight;

    /// <summary>The mid joint (elbow/knee) bend hint, measured like the position of <see cref="Transform"/>.</summary>
    public Float3 Pole;

    /// <summary>True if <see cref="Pole"/> holds a valid bend hint.</summary>
    public bool HasPole;
}
