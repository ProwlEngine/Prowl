namespace Prowl.Motion;

/// <summary>
/// The animation profile of a model. Wraps a <see cref="Skeleton"/>
/// and declares whether it is driven as a <see cref="AvatarType.Generic"/> rig (same-skeleton
/// playback only) or a <see cref="AvatarType.Humanoid"/> rig (retargetable through muscle space,
/// exposing a <see cref="HumanoidRig"/>). Built via <see cref="AvatarBuilder"/>.
/// </summary>
public sealed class Avatar
{
    private readonly Skeleton _skeleton;
    private readonly AvatarType _type;
    private readonly HumanoidRig? _humanoid;
    private readonly int _rootBoneIndex;

    internal Avatar(Skeleton skeleton, AvatarType type, HumanoidRig? humanoid, int rootBoneIndex)
    {
        _skeleton = skeleton;
        _type = type;
        _humanoid = humanoid;
        _rootBoneIndex = rootBoneIndex;
    }

    public Skeleton Skeleton => _skeleton;

    public AvatarType Type => _type;

    /// <summary>True if this avatar is a humanoid with a valid rig.</summary>
    public bool IsHuman => _type == AvatarType.Humanoid && _humanoid is not null;

    public bool IsValid => _type == AvatarType.Humanoid ? _humanoid is not null : _skeleton.IsValid;

    /// <summary>The humanoid rig, or null for a generic avatar.</summary>
    public HumanoidRig? Humanoid => _humanoid;

    /// <summary>The skeleton bone treated as the animation root (the hips for humanoids).</summary>
    public int RootBoneIndex => _rootBoneIndex;

    /// <summary>
    /// The auto-mapping result, when this avatar was built via <see cref="AvatarBuilder.BuildAutomatic(Skeleton)"/>.
    /// Holds the unmapped-bone warnings even for generic avatars (so an editor can show what was missing).
    /// Null for explicitly built avatars.
    /// </summary>
    public HumanoidMapResult? MappingReport { get; internal set; }
}
