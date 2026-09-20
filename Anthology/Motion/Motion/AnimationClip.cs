using System.Collections.Generic;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A skeletal animation clip storing one uncompressed transform per bone per key frame, plus
/// optional root motion, sync track and events. Sampling interpolates between key frames to produce
/// a <see cref="Pose"/>. See <see cref="CompressedAnimationClip"/> for the quantized storage format.
/// </summary>
public sealed class AnimationClip : AnimationClipBase
{
    private readonly Transform3D[] _frames; // flattened: frame * boneCount + bone
    private readonly float[] _floats;       // flattened: frame * channelCount + channel
    private readonly int _boneCount;
    private readonly int _channelCount;

    /// <summary>
    /// Builds a clip from key-frame poses (at least one), all for the given skeleton, with the clip
    /// length in seconds. Set <paramref name="isAdditive"/> if the poses store deltas rather than
    /// full poses. The key-frame poses are snapshotted; the caller may reuse them afterwards.
    /// </summary>
    public AnimationClip(
        Skeleton skeleton,
        IReadOnlyList<Pose> keyFrames,
        float durationSeconds,
        bool isAdditive = false,
        RootMotion? rootMotion = null,
        SyncTrack? syncTrack = null,
        IReadOnlyList<AnimationEvent>? events = null,
        IReadOnlyList<AnimationClipBase>? secondaryClips = null)
        : base(skeleton, CountFrames(keyFrames), durationSeconds, isAdditive, rootMotion, syncTrack, events, secondaryClips)
    {
        _boneCount = skeleton.BoneCount;
        _channelCount = skeleton.FloatChannelCount;
        _frames = new Transform3D[FrameCount * _boneCount];
        _floats = new float[FrameCount * _channelCount];
        for (int f = 0; f < FrameCount; f++)
        {
            Pose frame = keyFrames[f];
            if (frame.BoneCount != _boneCount)
                throw new ArgumentException("Every key frame must match the skeleton bone count.", nameof(keyFrames));

            int baseIndex = f * _boneCount;
            for (int b = 0; b < _boneCount; b++)
                _frames[baseIndex + b] = frame.GetTransform(b);

            int floatBase = f * _channelCount;
            for (int c = 0; c < _channelCount && c < frame.FloatChannelCount; c++)
                _floats[floatBase + c] = frame.GetFloat(c);
        }
    }

    internal static int CountFrames(IReadOnlyList<Pose> keyFrames)
    {
        ArgumentNullException.ThrowIfNull(keyFrames);
        return keyFrames.Count;
    }

    /// <summary>The key frame transform of a bone.</summary>
    public Transform3D GetKeyFrameTransform(int frame, int bone) => _frames[frame * _boneCount + bone];

    /// <summary>The key frame value of a float channel.</summary>
    public float GetKeyFrameFloat(int frame, int channel) => _floats[frame * _channelCount + channel];

    public override void GetPose(FrameTime frameTime, Pose result, SkeletonMapping? mapping)
    {
        ValidateTarget(result, mapping);

        int i0 = Math.Clamp(frameTime.FrameIndex, 0, FrameCount - 1);
        float frac = frameTime.Percentage;
        bool single = i0 >= FrameCount - 1 || !(frac > 0f);
        int a = i0 * _boneCount;
        int c1 = single ? a : (i0 + 1) * _boneCount;

        if (mapping is null)
        {
            for (int b = 0; b < _boneCount; b++)
                result.WriteLocal(b, single ? _frames[a + b] : Transform3D.Lerp(_frames[a + b], _frames[c1 + b], frac));
        }
        else
        {
            IReadOnlyList<Transform3D> reference = result.Skeleton.ParentSpaceReferencePose;
            for (int b = 0; b < result.BoneCount; b++)
            {
                int source = mapping.GetSourceBone(b);
                if (source == Skeleton.InvalidIndex)
                    result.WriteLocal(b, reference[b]);
                else
                    result.WriteLocal(b, single ? _frames[a + source] : Transform3D.Lerp(_frames[a + source], _frames[c1 + source], frac));
            }
        }

        SampleFloats(i0, single ? -1 : i0 + 1, frac, result, mapping);
        result.FinishWrite(IsAdditive ? PoseState.AdditivePose : PoseState.Pose);
    }

    private void SampleFloats(int frame, int nextFrame, float frac, Pose result, SkeletonMapping? mapping)
    {
        int count = result.FloatChannelCount;
        if (count == 0)
            return;

        int a = frame * _channelCount;
        int b = nextFrame < 0 ? a : nextFrame * _channelCount;
        for (int channel = 0; channel < count; channel++)
        {
            int source = mapping is null ? (channel < _channelCount ? channel : Skeleton.InvalidIndex) : mapping.GetSourceFloatChannel(channel);
            if (source == Skeleton.InvalidIndex)
            {
                result.WriteFloat(channel, 0f);
                continue;
            }
            float value = _floats[a + source];
            result.WriteFloat(channel, nextFrame < 0 ? value : value + (_floats[b + source] - value) * frac);
        }
    }
}
