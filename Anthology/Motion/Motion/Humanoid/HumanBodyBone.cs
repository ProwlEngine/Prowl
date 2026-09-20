namespace Prowl.Motion;

/// <summary>
/// The standard humanoid bones used for retargeting.
/// Body bones come first (their indices are stable), then
/// the per-hand fingers (three phalanges each). Fingers are optional and retarget by rotation only.
/// </summary>
public enum HumanBodyBone
{
    Hips,
    LeftUpperLeg,
    RightUpperLeg,
    LeftLowerLeg,
    RightLowerLeg,
    LeftFoot,
    RightFoot,
    Spine,
    Chest,
    UpperChest,
    Neck,
    Head,
    LeftShoulder,
    RightShoulder,
    LeftUpperArm,
    RightUpperArm,
    LeftLowerArm,
    RightLowerArm,
    LeftHand,
    RightHand,
    LeftToes,
    RightToes,
    LeftEye,
    RightEye,
    Jaw,

    // Fingers (optional). Proximal -> Intermediate -> Distal, parented to the hand.
    LeftThumbProximal,
    LeftThumbIntermediate,
    LeftThumbDistal,
    LeftIndexProximal,
    LeftIndexIntermediate,
    LeftIndexDistal,
    LeftMiddleProximal,
    LeftMiddleIntermediate,
    LeftMiddleDistal,
    LeftRingProximal,
    LeftRingIntermediate,
    LeftRingDistal,
    LeftLittleProximal,
    LeftLittleIntermediate,
    LeftLittleDistal,

    RightThumbProximal,
    RightThumbIntermediate,
    RightThumbDistal,
    RightIndexProximal,
    RightIndexIntermediate,
    RightIndexDistal,
    RightMiddleProximal,
    RightMiddleIntermediate,
    RightMiddleDistal,
    RightRingProximal,
    RightRingIntermediate,
    RightRingDistal,
    RightLittleProximal,
    RightLittleIntermediate,
    RightLittleDistal
}

/// <summary>
/// How an <see cref="Avatar"/> drives a skeleton. Generic plays back on the source skeleton only;
/// Humanoid is retargetable through muscle space.
/// </summary>
public enum AvatarType : byte
{
    Generic,
    Humanoid
}
