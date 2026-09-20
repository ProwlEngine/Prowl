using System.Collections.Generic;

namespace Prowl.Motion;

/// <summary>
/// Matches the bones and float channels of two skeletons by id, so a clip authored for one skeleton
/// plays on another rig that names its bones the same way. Target bones the source does not have keep
/// their reference pose. Build it once and reuse it: it holds no per frame state.
/// </summary>
public sealed class SkeletonMapping
{
    private readonly int[] _boneSource;
    private readonly int[] _channelSource;

    private SkeletonMapping(Skeleton source, Skeleton target, int[] boneSource, int[] channelSource, int mappedBoneCount)
    {
        Source = source;
        Target = target;
        _boneSource = boneSource;
        _channelSource = channelSource;
        MappedBoneCount = mappedBoneCount;
    }

    public Skeleton Source { get; }

    public Skeleton Target { get; }

    /// <summary>How many target bones found a source bone.</summary>
    public int MappedBoneCount { get; }

    /// <summary>True if no target bone matched, so sampling through this mapping only gives the reference pose.</summary>
    public bool IsEmpty => MappedBoneCount == 0;

    /// <summary>Matches every target bone and channel to the source id of the same name.</summary>
    public static SkeletonMapping Create(Skeleton source, Skeleton target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var boneSource = new int[target.BoneCount];
        int mapped = 0;
        for (int b = 0; b < boneSource.Length; b++)
        {
            boneSource[b] = source.GetBoneIndex(target.GetBoneID(b));
            if (boneSource[b] != Skeleton.InvalidIndex)
                mapped++;
        }

        var channelSource = new int[target.FloatChannelCount];
        for (int c = 0; c < channelSource.Length; c++)
            channelSource[c] = source.GetFloatChannelIndex(target.GetFloatChannelID(c));

        return new SkeletonMapping(source, target, boneSource, channelSource, mapped);
    }

    /// <summary>The source bone driving a target bone, or <see cref="Skeleton.InvalidIndex"/>.</summary>
    public int GetSourceBone(int targetBoneIndex) => _boneSource[targetBoneIndex];

    /// <summary>The source channel driving a target channel, or <see cref="Skeleton.InvalidIndex"/>.</summary>
    public int GetSourceFloatChannel(int targetChannelIndex) => _channelSource[targetChannelIndex];

    /// <summary>Target bones that found no source bone, by id (for reporting what a clip will not drive).</summary>
    public IEnumerable<StringID> UnmappedTargetBones()
    {
        for (int b = 0; b < _boneSource.Length; b++)
            if (_boneSource[b] == Skeleton.InvalidIndex)
                yield return Target.GetBoneID(b);
    }
}
