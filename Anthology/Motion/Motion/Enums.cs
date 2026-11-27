namespace Prowl.Motion;

/// <summary>
/// Skeleton level of detail. Low LOD samples only the leading subset of bones.
/// </summary>
public enum SkeletonLOD : byte
{
    Low,
    High
}

/// <summary>
/// The semantic state of a <see cref="Pose"/>. Gates how the pose participates in
/// blending.
/// </summary>
public enum PoseState : byte
{
    Unset,
    Pose,
    ReferencePose,
    ZeroPose,
    AdditivePose
}

/// <summary>
/// Per-bone flags carried by the skeleton.
/// </summary>
[Flags]
public enum BoneFlags
{
    None = 0
}
