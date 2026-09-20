using System;
using System.Collections.Generic;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A lightweight animator that plays clips with optional cross fades, without the graph/state machine
/// machinery (the engine equivalent of a simple clip player). Cross fades stack: fading to a new clip
/// while a fade is running blends from the current mix. Subclass it and implement the
/// <see cref="AnimatorBase"/> hooks to bind it to an engine.
/// </summary>
public abstract class SimpleAnimator : AnimatorBase
{
    private readonly List<ClipState> _layers = new();
    private readonly Stack<ClipState> _pool = new();

    private Skeleton[] _secondarySkeletons = Array.Empty<Skeleton>();
    private Pose[] _secondaryPoses = Array.Empty<Pose>();
    private AnimationClipBase[] _secondaryClips = Array.Empty<AnimationClipBase>();
    private AnimationClipBase? _secondarySource;

    protected SimpleAnimator(Skeleton skeleton, Avatar? avatar = null) : base(skeleton, avatar)
    {
    }

    /// <summary>The secondary skeletons driven by the current clip (e.g. a held weapon).</summary>
    public IReadOnlyList<Skeleton> SecondarySkeletons => _secondarySkeletons;

    /// <summary>The secondary poses (parallel to <see cref="SecondarySkeletons"/>), valid after Update.</summary>
    public IReadOnlyList<Pose> SecondaryPoses => _secondaryPoses;

    /// <summary>The clip currently playing (the destination clip while cross-fading), or null.</summary>
    public AnimationClipBase? CurrentClip => _layers.Count > 0 ? _layers[^1].Clip : null;

    /// <summary>True while a cross-fade is in progress.</summary>
    public bool IsCrossFading => _layers.Count > 1;

    /// <summary>Plays a clip immediately, cancelling any cross-fade.</summary>
    public void Play(AnimationClipBase clip, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ClearLayers();
        _layers.Add(Rent(clip, loop, 0f));
    }

    /// <summary>
    /// Cross fades to a new clip over <paramref name="duration"/> seconds. A fade already in progress
    /// keeps playing underneath, so the new clip blends in from the current mix.
    /// </summary>
    public void CrossFade(AnimationClipBase clip, float duration, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (_layers.Count == 0 || !(duration > 0f))
        {
            Play(clip, loop);
            return;
        }
        _layers.Add(Rent(clip, loop, duration));
    }

    protected override void Evaluate(float scaledDeltaTime, out Transform3D rootMotionDelta)
    {
        if (_layers.Count == 0)
        {
            Pose.SetToReferencePose();
            rootMotionDelta = Transform3D.Identity;
            return;
        }

        float fadeDelta = MathF.Abs(scaledDeltaTime);
        rootMotionDelta = Transform3D.Identity;
        int fullyFadedIn = 0;
        for (int i = 0; i < _layers.Count; i++)
        {
            ClipState layer = _layers[i];
            Transform3D layerRootMotion = layer.Advance(scaledDeltaTime);
            float weight = layer.AdvanceFade(fadeDelta);

            if (i == 0)
            {
                Pose.CopyFrom(layer.Pose);
                rootMotionDelta = layerRootMotion;
            }
            else
            {
                Blender.Blend(Pose, Pose, layer.Pose, weight);
                rootMotionDelta = Blender.BlendRootMotionDeltas(rootMotionDelta, layerRootMotion, weight);
            }

            if (weight >= 1f)
                fullyFadedIn = i;
        }

        for (int i = 0; i < fullyFadedIn; i++)
            _pool.Push(_layers[i]);
        _layers.RemoveRange(0, fullyFadedIn);

        SampleSecondaries();
    }

    /// <summary>
    /// Pushes one secondary bone's local transform to the engine (e.g. a held weapon's bone). Override
    /// to drive the secondary skeleton's objects; default is a no-op.
    /// </summary>
    protected virtual void ApplySecondaryBoneTransform(int secondaryIndex, Skeleton skeleton, int boneIndex, in Transform3D localTransform) { }

    private void SampleSecondaries()
    {
        ClipState top = _layers[^1];
        AnimationClipBase clip = top.Clip;
        if (!clip.HasSecondaryClips)
        {
            if (_secondarySource is not null)
            {
                _secondarySkeletons = Array.Empty<Skeleton>();
                _secondaryPoses = Array.Empty<Pose>();
                _secondaryClips = Array.Empty<AnimationClipBase>();
                _secondarySource = null;
            }
            return;
        }

        if (!ReferenceEquals(clip, _secondarySource))
            RebuildSecondaryBuffers(clip);

        for (int i = 0; i < _secondaryClips.Length; i++)
        {
            _secondaryClips[i].GetPose(top.NormalizedTime, _secondaryPoses[i]);
            _secondaryPoses[i].CalculateModelSpaceTransforms();
        }
    }

    private void RebuildSecondaryBuffers(AnimationClipBase clip)
    {
        IReadOnlyList<AnimationClipBase> secondaries = clip.SecondaryClips;
        _secondaryClips = new AnimationClipBase[secondaries.Count];
        _secondarySkeletons = new Skeleton[secondaries.Count];
        _secondaryPoses = new Pose[secondaries.Count];
        for (int i = 0; i < secondaries.Count; i++)
        {
            _secondaryClips[i] = secondaries[i];
            _secondarySkeletons[i] = secondaries[i].Skeleton;
            _secondaryPoses[i] = new Pose(secondaries[i].Skeleton);
        }
        _secondarySource = clip;
    }

    protected override void AfterPosePushed()
    {
        for (int s = 0; s < _secondaryClips.Length; s++)
        {
            Skeleton skeleton = _secondarySkeletons[s];
            Pose pose = _secondaryPoses[s];
            for (int b = 0; b < skeleton.BoneCount; b++)
                ApplySecondaryBoneTransform(s, skeleton, b, pose.GetTransform(b));
        }
    }

    private ClipState Rent(AnimationClipBase clip, bool loop, float fadeDuration)
    {
        ClipState state = _pool.Count > 0 ? _pool.Pop() : new ClipState(Skeleton);
        state.Start(clip, loop, fadeDuration);
        return state;
    }

    private void ClearLayers()
    {
        foreach (ClipState layer in _layers)
            _pool.Push(layer);
        _layers.Clear();
    }

    /// <summary>Per clip playback cursor: tracks time, fade progress, pose and root motion delta.</summary>
    private sealed class ClipState
    {
        private ClipCursor _cursor;
        private bool _loop;
        private bool _firstUpdate;
        private float _fadeTime;
        private float _fadeDuration;

        private SkeletonMapping? _mapping;

        public ClipState(Skeleton skeleton) => Pose = new Pose(skeleton);

        public AnimationClipBase Clip { get; private set; } = null!;
        public Pose Pose { get; }
        public float NormalizedTime => _cursor.Time;

        public void Start(AnimationClipBase clip, bool loop, float fadeDuration)
        {
            Clip = clip;
            _mapping = clip.GetMappingTo(Pose.Skeleton);
            _loop = loop;
            _cursor = default;
            _firstUpdate = true;
            _fadeTime = 0f;
            _fadeDuration = fadeDuration;
        }

        public Transform3D Advance(float deltaTime)
        {
            PlaybackSpan span = _cursor.Advance(deltaTime, Clip.Duration, _loop, _firstUpdate);
            _firstUpdate = false;
            Clip.GetPose(_cursor.Time, Pose, _mapping);
            return Clip.GetRootMotionDelta(span);
        }

        /// <summary>Advances the fade and returns its weight (1 for a layer that is not fading).</summary>
        public float AdvanceFade(float deltaTime)
        {
            if (_fadeDuration <= 0f)
                return 1f;
            _fadeTime += deltaTime;
            return Math.Clamp(_fadeTime / _fadeDuration, 0f, 1f);
        }
    }
}
