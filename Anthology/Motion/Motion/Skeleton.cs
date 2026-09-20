using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// An immutable shared skeleton: parallel arrays of bone ids, parent indices, and a
/// parent-space reference pose, plus a derived model-space reference pose.
/// </summary>
public sealed class Skeleton
{
    /// <summary>Sentinel returned by lookups that find no bone.</summary>
    public const int InvalidIndex = -1;

    private readonly StringID[] _boneIds;
    private readonly int[] _parentIndices;
    private readonly Transform3D[] _parentSpaceReferencePose;
    private readonly Transform3D[] _modelSpaceReferencePose;
    private readonly int[] _firstChild;
    private readonly Dictionary<uint, int> _idToIndex;
    private readonly int _numBonesToSampleAtLowLOD;
    private readonly BoneHierarchy _hierarchy;
    private readonly int[] _lowLodOrder;
    private readonly bool[] _inLowLodSet;
    private readonly bool _hasDuplicateIds;
    private readonly StringID[] _floatChannelIds;
    private readonly Dictionary<uint, int> _floatChannelToIndex;

    /// <summary>
    /// Builds a skeleton. <paramref name="parentIndices"/> uses <see cref="InvalidIndex"/> for roots.
    /// The parent-space reference pose has one transform per bone. If
    /// <paramref name="numBonesToSampleAtLowLOD"/> is negative, all bones are high LOD.
    /// <paramref name="floatChannelIds"/> names extra scalar channels carried alongside the bones
    /// (blend shape weights and the like), which rest at 0 and blend with the pose.
    /// </summary>
    public Skeleton(
        IReadOnlyList<StringID> boneIds,
        IReadOnlyList<int> parentIndices,
        IReadOnlyList<Transform3D> parentSpaceReferencePose,
        int numBonesToSampleAtLowLOD = -1,
        IReadOnlyList<StringID>? floatChannelIds = null)
    {
        ArgumentNullException.ThrowIfNull(boneIds);
        ArgumentNullException.ThrowIfNull(parentIndices);
        ArgumentNullException.ThrowIfNull(parentSpaceReferencePose);

        int count = boneIds.Count;
        if (parentIndices.Count != count || parentSpaceReferencePose.Count != count)
            throw new ArgumentException("Skeleton array lengths must match.");

        _boneIds = new StringID[count];
        _parentIndices = new int[count];
        _parentSpaceReferencePose = new Transform3D[count];
        _firstChild = new int[count];
        _idToIndex = new Dictionary<uint, int>(count);

        for (int i = 0; i < count; i++)
        {
            _boneIds[i] = boneIds[i];
            _parentIndices[i] = parentIndices[i];
            _parentSpaceReferencePose[i] = parentSpaceReferencePose[i];
            _firstChild[i] = InvalidIndex;
            if (!_idToIndex.TryAdd(boneIds[i].ID, i))
                _hasDuplicateIds = true;
        }

        _hierarchy = new BoneHierarchy(_parentIndices);

        for (int i = 0; i < count; i++)
        {
            int p = _hierarchy.Parents[i];
            if (p != InvalidIndex && _firstChild[p] == InvalidIndex)
                _firstChild[p] = i;
        }

        _numBonesToSampleAtLowLOD = numBonesToSampleAtLowLOD < 0 ? count : Math.Min(numBonesToSampleAtLowLOD, count);
        _lowLodOrder = _hierarchy.SubsetOrder(_numBonesToSampleAtLowLOD);
        _inLowLodSet = new bool[count];
        foreach (int bone in _lowLodOrder)
            _inLowLodSet[bone] = true;
        _modelSpaceReferencePose = new Transform3D[count];
        TransformOps.ComputeModelSpace(_parentSpaceReferencePose, _hierarchy.Order, _hierarchy.Parents, _modelSpaceReferencePose);

        _floatChannelIds = floatChannelIds is null ? Array.Empty<StringID>() : floatChannelIds.ToArray();
        _floatChannelToIndex = new Dictionary<uint, int>(_floatChannelIds.Length);
        for (int i = 0; i < _floatChannelIds.Length; i++)
            _floatChannelToIndex.TryAdd(_floatChannelIds[i].ID, i);
    }

    /// <summary>
    /// True if the skeleton is internally consistent: parents in range and acyclic, and every bone id
    /// unique (two bones with the same name or colliding hashes make id lookups ambiguous).
    /// </summary>
    public bool IsValid
    {
        get
        {
            int count = _boneIds.Length;
            if (count == 0 || _hierarchy.HasCycle || _hasDuplicateIds) return false;
            for (int i = 0; i < count; i++)
            {
                int p = _parentIndices[i];
                if (p == InvalidIndex) continue;
                if (p < 0 || p >= count || p == i) return false;
            }
            return true;
        }
    }

    /// <summary>True if two bones share an id (a duplicate name or a hash collision), lookups return the first.</summary>
    public bool HasDuplicateBoneIds => _hasDuplicateIds;

    /// <summary>Parents first bone order with invalid and cyclic parent links cut.</summary>
    internal int[] EvaluationOrder => _hierarchy.Order;

    /// <summary>Parents first order of the low LOD bones and their ancestors.</summary>
    internal int[] LowLodEvaluationOrder => _lowLodOrder;

    /// <summary>Parent indices with invalid and cyclic links replaced by <see cref="InvalidIndex"/>.</summary>
    internal int[] SanitizedParentIndices => _hierarchy.Parents;

    /// <summary>True if <paramref name="boneIndex"/> is a low LOD bone or an ancestor of one.</summary>
    internal bool IsInLowLodSet(int boneIndex) => _inLowLodSet[boneIndex];

    public int BoneCount => _boneIds.Length;

    public int GetBoneCount(SkeletonLOD lod) => lod == SkeletonLOD.Low ? _numBonesToSampleAtLowLOD : _boneIds.Length;

    public bool IsValidBoneIndex(int boneIndex) => boneIndex >= 0 && boneIndex < _boneIds.Length;

    /// <summary>Returns the bone index for an id, or <see cref="InvalidIndex"/>.</summary>
    public int GetBoneIndex(StringID id) => _idToIndex.TryGetValue(id.ID, out int index) ? index : InvalidIndex;

    public StringID GetBoneID(int boneIndex) => _boneIds[boneIndex];

    /// <summary>Direct parent index, or <see cref="InvalidIndex"/> for a root.</summary>
    public int GetParentBoneIndex(int boneIndex) => _parentIndices[boneIndex];

    public IReadOnlyList<int> ParentBoneIndices => _parentIndices;

    /// <summary>First child encountered, or <see cref="InvalidIndex"/> if this is a leaf.</summary>
    public int GetFirstChildBoneIndex(int boneIndex) => _firstChild[boneIndex];

    /// <summary>True if <paramref name="childBoneIndex"/> is anywhere below <paramref name="parentBoneIndex"/>.</summary>
    public bool IsChildBoneOf(int parentBoneIndex, int childBoneIndex)
    {
        int current = _hierarchy.Parents[childBoneIndex];
        while (current != InvalidIndex)
        {
            if (current == parentBoneIndex) return true;
            current = _hierarchy.Parents[current];
        }
        return false;
    }

    public bool IsLeafBone(int boneIndex) => _firstChild[boneIndex] == InvalidIndex;

    public SkeletonLOD GetBoneLOD(int boneIndex) => boneIndex < _numBonesToSampleAtLowLOD ? SkeletonLOD.Low : SkeletonLOD.High;

    public IReadOnlyList<Transform3D> ParentSpaceReferencePose => _parentSpaceReferencePose;

    public IReadOnlyList<Transform3D> ModelSpaceReferencePose => _modelSpaceReferencePose;

    /// <summary>Parent-space (local) reference transform of a bone.</summary>
    public Transform3D GetBoneParentSpaceTransform(int boneIndex) => _parentSpaceReferencePose[boneIndex];

    /// <summary>Model-space (global) reference transform of a bone.</summary>
    public Transform3D GetBoneModelSpaceTransform(int boneIndex) => _modelSpaceReferencePose[boneIndex];

    /// <summary>Number of bones sampled at the low LOD.</summary>
    public int LowLodBoneCount => _numBonesToSampleAtLowLOD;

    /// <summary>Number of scalar channels carried alongside the bones.</summary>
    public int FloatChannelCount => _floatChannelIds.Length;

    public IReadOnlyList<StringID> FloatChannelIds => _floatChannelIds;

    public StringID GetFloatChannelID(int channelIndex) => _floatChannelIds[channelIndex];

    /// <summary>Returns the channel index for an id, or <see cref="InvalidIndex"/>.</summary>
    public int GetFloatChannelIndex(StringID id) => _floatChannelToIndex.TryGetValue(id.ID, out int index) ? index : InvalidIndex;
}
