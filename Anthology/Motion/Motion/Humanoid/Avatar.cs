namespace Prowl.Motion;

/// <summary>
/// The animation profile of a model: a <see cref="Skeleton"/>, as a generic rig or a humanoid one
/// retargetable through muscle space. Built via <see cref="AvatarBuilder"/>.
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

    /// <summary>The auto mapping result, with its unmapped bone warnings. Null for explicitly built avatars.</summary>
    public HumanoidMapResult? MappingReport { get; internal set; }
}
