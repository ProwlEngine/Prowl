using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A flag-tagged target: either a literal world transform or a named bone with an optional
/// bone-space offset. Resolved against a <see cref="Pose"/>. Targets carry rotation and translation
/// only (scale is ignored).
/// </summary>
public readonly struct Target
{
    private readonly bool _isSet;
    private readonly bool _isBoneTarget;
    private readonly int _boneIndex;
    private readonly Transform3D _transform; // world transform, or bone-space offset for bone targets

    private Target(bool isSet, bool isBoneTarget, int boneIndex, Transform3D transform)
    {
        _isSet = isSet;
        _isBoneTarget = isBoneTarget;
        _boneIndex = boneIndex;
        _transform = transform;
    }

    /// <summary>A target that tracks a bone's model-space transform.</summary>
    public static Target FromBone(int boneIndex) => new(true, true, boneIndex, Transform3D.Identity);

    /// <summary>A target that tracks a bone's model-space transform with a bone-space offset.</summary>
    public static Target FromBone(int boneIndex, Transform3D boneSpaceOffset) => new(true, true, boneIndex, boneSpaceOffset);

    /// <summary>A target fixed to a world transform.</summary>
    public static Target FromWorld(Transform3D worldTransform) => new(true, false, Skeleton.InvalidIndex, worldTransform);

    /// <summary>True if this target holds a value.</summary>
    public bool IsSet => _isSet;

    /// <summary>True if this target tracks a bone (vs a fixed world transform).</summary>
    public bool IsBoneTarget => _isBoneTarget;

    /// <summary>The tracked bone index (only meaningful when <see cref="IsBoneTarget"/>).</summary>
    public int BoneIndex => _boneIndex;

    /// <summary>
    /// Returns a copy of this bone target with an additional offset folded into its bone-space offset
    /// (no-op for world targets or unset targets). Used by the target-offset value node.
    /// </summary>
    public Target WithBoneOffset(Quaternion rotationOffset, Float3 translationOffset)
    {
        if (!_isSet || !_isBoneTarget)
            return this;
        var added = new Transform3D(translationOffset, rotationOffset, Float3.One);
        return new Target(true, true, _boneIndex, TransformOps.Combine(_transform, added));
    }

    /// <summary>Resolves the target to a transform, using the pose for bone targets. Scale is identity.</summary>
    public bool TryGetTransform(Pose pose, out Transform3D result)
    {
        if (!_isSet)
        {
            result = Transform3D.Identity;
            return false;
        }

        if (_isBoneTarget)
        {
            if (pose is null || !pose.Skeleton.IsValidBoneIndex(_boneIndex))
            {
                result = Transform3D.Identity;
                return false;
            }

            Transform3D resolved = TransformOps.Combine(pose.GetModelSpaceTransform(_boneIndex), _transform);
            result = new Transform3D(resolved.position, resolved.rotation, Float3.One);
            return true;
        }

        result = new Transform3D(_transform.position, _transform.rotation, Float3.One);
        return true;
    }
}
