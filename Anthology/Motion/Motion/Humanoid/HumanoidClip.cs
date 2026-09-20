using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A clip baked into muscle space: one <see cref="HumanPose"/> per frame instead of one skeleton pose,
/// so it plays on any humanoid avatar whatever its proportions or bind pose.
/// <see cref="Bind"/> turns it into an ordinary clip for one avatar, which the graph and the animators
/// play like any other. Baking costs a retarget per frame, playback costs one per sample.
/// </summary>
public sealed class HumanoidClip
{
    private readonly float[] _muscles;      // flattened: frame * MuscleCount + muscle
    private readonly Float3[] _bodyPosition;
    private readonly Quaternion[] _bodyRotation;
    private readonly HumanGoalState[] _goals; // flattened: frame * GoalCount + goal
    private readonly StringID[] _channelIds;
    private readonly float[] _channels;        // flattened: frame * ChannelCount + channel
    private readonly int _muscleCount;

    private HumanoidClip(
        int frameCount,
        float durationSeconds,
        float sourceScale,
        float[] muscles,
        Float3[] bodyPosition,
        Quaternion[] bodyRotation,
        HumanGoalState[] goals,
        StringID[] channelIds,
        float[] channels,
        RootMotion? rootMotion,
        SyncTrack? syncTrack,
        IReadOnlyList<AnimationEvent>? events)
    {
        FrameCount = frameCount;
        Duration = durationSeconds;
        SourceScale = sourceScale;
        _muscleCount = HumanTrait.MuscleCount;
        _muscles = muscles;
        _bodyPosition = bodyPosition;
        _bodyRotation = bodyRotation;
        _goals = goals;
        _channelIds = channelIds;
        _channels = channels;
        RootMotion = rootMotion;
        SyncTrack = syncTrack ?? SyncTrack.Default;
        Events = events is null ? Array.Empty<AnimationEvent>() : events.ToArray();
    }

    public int FrameCount { get; }

    public float Duration { get; }

    /// <summary>The avatar scale the clip was baked from, used to rescale root motion onto a different rig.</summary>
    public float SourceScale { get; }

    public RootMotion? RootMotion { get; }

    public SyncTrack SyncTrack { get; }

    public IReadOnlyList<AnimationEvent> Events { get; }

    /// <summary>
    /// The scalar channels baked alongside the muscles, by the name they had on the source rig. Muscle
    /// space says nothing about blend shapes or visemes, so they travel beside it and are matched by name
    /// when the clip is bound.
    /// </summary>
    public IReadOnlyList<StringID> FloatChannelIds => _channelIds;

    /// <summary>Samples one stored channel, interpolating between frames.</summary>
    public float GetFloatChannel(FrameTime frameTime, int channelIndex)
    {
        int count = _channelIds.Length;
        int i0 = Math.Clamp(frameTime.FrameIndex, 0, FrameCount - 1);
        float frac = frameTime.Percentage;
        float value = _channels[i0 * count + channelIndex];
        if (i0 >= FrameCount - 1 || !(frac > 0f))
            return value;
        return value + (_channels[(i0 + 1) * count + channelIndex] - value) * frac;
    }

    internal ReadOnlySpan<float> FrameFloats(int frame) => _channels.AsSpan(frame * _channelIds.Length, _channelIds.Length);

    /// <summary>
    /// Bakes a skeletal clip into muscle space through its source avatar, which must be humanoid. The
    /// clip is sampled at its own key frames, so nothing is resampled.
    /// </summary>
    public static HumanoidClip Bake(Avatar sourceAvatar, AnimationClipBase clip)
    {
        ArgumentNullException.ThrowIfNull(sourceAvatar);
        ArgumentNullException.ThrowIfNull(clip);
        if (!sourceAvatar.IsHuman)
            throw new ArgumentException("Baking to muscle space needs a humanoid avatar.", nameof(sourceAvatar));

        SkeletonMapping? mapping = clip.GetMappingTo(sourceAvatar.Skeleton);
        int frames = clip.FrameCount;
        int muscleCount = HumanTrait.MuscleCount;

        var muscles = new float[frames * muscleCount];
        var bodyPosition = new Float3[frames];
        var bodyRotation = new Quaternion[frames];
        var goals = new HumanGoalState[frames * HumanPose.GoalCount];

        Skeleton source = sourceAvatar.Skeleton;
        int channelCount = source.FloatChannelCount;
        var channelIds = new StringID[channelCount];
        for (int c = 0; c < channelCount; c++)
            channelIds[c] = source.GetFloatChannelID(c);
        var channels = new float[frames * channelCount];

        var pose = new Pose(source);
        var human = new HumanPose();
        for (int f = 0; f < frames; f++)
        {
            clip.GetPose(new FrameTime(f, 0f), pose, mapping);
            Retargeter.RetargetFrom(sourceAvatar, pose, human);

            human.Muscles.CopyTo(muscles.AsSpan(f * muscleCount, muscleCount));
            bodyPosition[f] = human.BodyPosition;
            bodyRotation[f] = human.BodyRotation;
            for (int g = 0; g < HumanPose.GoalCount; g++)
                goals[f * HumanPose.GoalCount + g] = human.GetGoal((HumanGoal)g);
            for (int c = 0; c < channelCount; c++)
                channels[f * channelCount + c] = pose.GetFloat(c);
        }

        return new HumanoidClip(frames, clip.Duration, sourceAvatar.Humanoid!.Scale, muscles, bodyPosition, bodyRotation, goals,
            channelIds, channels, clip.RootMotion, clip.SyncTrack, clip.Events);
    }

    /// <summary>
    /// Builds a clip straight from muscle space frames, for a host loading one it saved earlier.
    /// <paramref name="sourceScale"/> is the avatar scale it was baked from, used to rescale root motion.
    /// </summary>
    public static HumanoidClip FromFrames(
        IReadOnlyList<HumanPose> frames,
        float durationSeconds,
        float sourceScale,
        RootMotion? rootMotion = null,
        SyncTrack? syncTrack = null,
        IReadOnlyList<AnimationEvent>? events = null,
        IReadOnlyList<StringID>? floatChannelIds = null,
        IReadOnlyList<float>? floatValues = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
            throw new ArgumentException("A humanoid clip needs at least one frame.", nameof(frames));

        StringID[] channelIds = floatChannelIds is null ? Array.Empty<StringID>() : floatChannelIds.ToArray();
        var channels = new float[frames.Count * channelIds.Length];
        if (channelIds.Length > 0)
        {
            if (floatValues is null || floatValues.Count != channels.Length)
                throw new ArgumentException("A channel value is needed for every channel of every frame.", nameof(floatValues));
            for (int i = 0; i < channels.Length; i++)
                channels[i] = floatValues[i];
        }

        int muscleCount = HumanTrait.MuscleCount;
        var muscles = new float[frames.Count * muscleCount];
        var bodyPosition = new Float3[frames.Count];
        var bodyRotation = new Quaternion[frames.Count];
        var goals = new HumanGoalState[frames.Count * HumanPose.GoalCount];

        for (int f = 0; f < frames.Count; f++)
        {
            HumanPose human = frames[f];
            human.Muscles.CopyTo(muscles.AsSpan(f * muscleCount, muscleCount));
            bodyPosition[f] = human.BodyPosition;
            bodyRotation[f] = human.BodyRotation;
            for (int g = 0; g < HumanPose.GoalCount; g++)
                goals[f * HumanPose.GoalCount + g] = human.GetGoal((HumanGoal)g);
        }

        return new HumanoidClip(frames.Count, durationSeconds, sourceScale, muscles, bodyPosition, bodyRotation, goals,
            channelIds, channels, rootMotion, syncTrack, events);
    }

    /// <summary>Maps a normalized time in [0,1] to a frame time.</summary>
    public FrameTime GetFrameTime(float percentageThrough)
    {
        if (FrameCount <= 1)
            return new FrameTime(0, 0f);

        float clamped = float.IsFinite(percentageThrough) ? Maths.Clamp(percentageThrough, 0f, 1f) : 0f;
        float frameValue = clamped * (FrameCount - 1);
        int index = (int)MathF.Floor(frameValue);
        return index >= FrameCount - 1 ? new FrameTime(FrameCount - 1, 0f) : new FrameTime(index, frameValue - index);
    }

    /// <summary>Samples the baked muscle pose, interpolating between frames.</summary>
    public void GetHumanPose(FrameTime frameTime, HumanPose result)
    {
        ArgumentNullException.ThrowIfNull(result);

        int i0 = Math.Clamp(frameTime.FrameIndex, 0, FrameCount - 1);
        float frac = frameTime.Percentage;
        bool single = i0 >= FrameCount - 1 || !(frac > 0f);
        int i1 = single ? i0 : i0 + 1;

        Span<float> target = result.Muscles;
        int a = i0 * _muscleCount;
        int b = i1 * _muscleCount;
        for (int m = 0; m < _muscleCount; m++)
        {
            float value = _muscles[a + m];
            target[m] = single ? value : value + (_muscles[b + m] - value) * frac;
        }

        result.BodyPosition = single ? _bodyPosition[i0] : Maths.Lerp(_bodyPosition[i0], _bodyPosition[i1], frac);
        result.BodyRotation = single ? _bodyRotation[i0] : Quaternion.Slerp(_bodyRotation[i0], _bodyRotation[i1], frac);

        for (int g = 0; g < HumanPose.GoalCount; g++)
        {
            HumanGoalState from = _goals[i0 * HumanPose.GoalCount + g];
            if (!single)
            {
                HumanGoalState to = _goals[i1 * HumanPose.GoalCount + g];
                from.Transform = Transform3D.Lerp(from.Transform, to.Transform, frac);
                from.Pole = Maths.Lerp(from.Pole, to.Pole, frac);
                from.PositionWeight += (to.PositionWeight - from.PositionWeight) * frac;
                from.RotationWeight += (to.RotationWeight - from.RotationWeight) * frac;
            }
            result.SetGoal((HumanGoal)g, from);
        }
    }

    /// <summary>Samples the baked muscle pose at a normalized time in [0,1].</summary>
    public void GetHumanPose(float percentageThrough, HumanPose result) => GetHumanPose(GetFrameTime(percentageThrough), result);

    /// <summary>
    /// Binds the clip to one humanoid avatar, giving an ordinary clip on that avatar's skeleton.
    /// Root motion is rescaled by the two avatars' scales so a bigger rig covers proportionally more ground.
    /// </summary>
    public AnimationClipBase Bind(Avatar avatar)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        if (!avatar.IsHuman)
            throw new ArgumentException("A humanoid clip only plays on a humanoid avatar.", nameof(avatar));

        return new BoundHumanoidClip(this, avatar, ScaleRootMotion(avatar.Humanoid!.Scale));
    }

    private RootMotion? ScaleRootMotion(float targetScale)
    {
        if (RootMotion is null)
            return null;
        if (!(SourceScale > 0f) || Maths.Abs(targetScale - SourceScale) < 1e-6f)
            return RootMotion;

        float ratio = targetScale / SourceScale;
        IReadOnlyList<Transform3D> frames = RootMotion.Frames;
        var scaled = new Transform3D[frames.Count];
        for (int i = 0; i < scaled.Length; i++)
            scaled[i] = new Transform3D(frames[i].position * ratio, frames[i].rotation, frames[i].scale);
        return new RootMotion(scaled, RootMotion.Duration);
    }

    /// <summary>A humanoid clip playing on one avatar: every sample decodes muscle space onto its skeleton.</summary>
    private sealed class BoundHumanoidClip : AnimationClipBase
    {
        [ThreadStatic] private static HumanPose? s_human;

        private readonly HumanoidClip _source;
        private readonly Avatar _avatar;
        private readonly int[] _channelSource;

        public BoundHumanoidClip(HumanoidClip source, Avatar avatar, RootMotion? rootMotion)
            : base(avatar.Skeleton, source.FrameCount, source.Duration, false, rootMotion, source.SyncTrack, source.Events, null)
        {
            _source = source;
            _avatar = avatar;

            // Channels come from another rig, so they are matched by name the way bones are.
            Skeleton target = avatar.Skeleton;
            _channelSource = new int[target.FloatChannelCount];
            for (int c = 0; c < _channelSource.Length; c++)
            {
                _channelSource[c] = Skeleton.InvalidIndex;
                StringID id = target.GetFloatChannelID(c);
                for (int s = 0; s < source._channelIds.Length; s++)
                {
                    if (source._channelIds[s] != id)
                        continue;
                    _channelSource[c] = s;
                    break;
                }
            }
        }

        public override void GetPose(FrameTime frameTime, Pose result, SkeletonMapping? mapping)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (mapping is not null)
                throw new ArgumentException("A humanoid clip retargets by itself, so bind it to the avatar it plays on.", nameof(mapping));
            if (result.BoneCount != Skeleton.BoneCount)
                throw new ArgumentException("Result pose bone count must match the avatar skeleton.", nameof(result));

            HumanPose human = s_human ??= new HumanPose();
            _source.GetHumanPose(frameTime, human);
            Retargeter.RetargetTo(_avatar, human, result);

            int count = Math.Min(result.FloatChannelCount, _channelSource.Length);
            for (int c = 0; c < count; c++)
            {
                int source = _channelSource[c];
                result.SetFloat(c, source == Skeleton.InvalidIndex ? 0f : _source.GetFloatChannel(frameTime, source));
            }
            for (int c = count; c < result.FloatChannelCount; c++)
                result.SetFloat(c, 0f);
        }
    }
}
