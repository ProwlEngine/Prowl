using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>Samples per update root deltas from a warped trajectory (character space frames relative to the first).</summary>
internal static class WarpedTrajectory
{
    /// <summary>The delta a playback span covers, following its direction and every loop it wraps.</summary>
    public static Transform3D Delta(Transform3D[] frames, in PlaybackSpan span)
    {
        if (span.Wraps == 0)
            return Between(frames, span.From, span.To);

        float end = span.Backward ? 0f : 1f;
        float start = span.Backward ? 1f : 0f;
        Transform3D delta = Between(frames, span.From, end);
        Transform3D fullLoop = Between(frames, start, end);
        for (int loop = 1; loop < span.Wraps; loop++)
            delta = TransformOps.Combine(delta, fullLoop);
        return TransformOps.Combine(delta, Between(frames, start, span.To));
    }

    private static Transform3D Between(Transform3D[] frames, float fromN, float toN)
        => TransformOps.Delta(Sample(frames, fromN), Sample(frames, toN));

    public static Transform3D Sample(Transform3D[] frames, float normalized)
    {
        int last = frames.Length - 1;
        if (last <= 0)
            return frames.Length == 1 ? frames[0] : Transform3D.Identity;
        float f = Maths.Clamp(normalized, 0f, 1f) * last;
        int i0 = (int)MathF.Floor(f);
        if (i0 >= last)
            return frames[last];
        return Transform3D.Lerp(frames[i0], frames[i0 + 1], f - i0);
    }
}

// ---------------------------------------------------------------------------------------------
// Orientation warp (re-heads a clip's root motion toward a desired direction / by an angle)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Wraps a clip and rewrites its root motion so the travel after the warp window heads toward a
/// target (a character space Float3 direction, or a float angle offset in degrees about up that
/// turns the clip's own travel) while leaving the pose untouched. The turn is spread over the first
/// <see cref="OrientationWarpEvent"/> on the clip. With no such event the whole path turns at the
/// start. The warp is computed once, on the first update or after a reseek.
/// </summary>
public sealed class OrientationWarpDefinition : PoseNodeDefinition
{
    public OrientationWarpDefinition(int clipChild, int targetNodeIndex, bool isAngleOffset)
    {
        ClipChild = clipChild;
        TargetNodeIndex = targetNodeIndex;
        IsAngleOffset = isAngleOffset;
    }

    public int ClipChild { get; }
    public int TargetNodeIndex { get; }

    /// <summary>If true the target value is a float angle offset (degrees); otherwise a Float3 direction.</summary>
    public bool IsAngleOffset { get; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly OrientationWarpDefinition _def;
        private ClipNodeInstance _clip = null!;
        private ValueNodeInstance _target = null!;
        private float _windowStart, _windowEnd;
        private Transform3D[]? _warped;
        private bool _needsWarp;

        public Instance(OrientationWarpDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.ClipChild);
            if (Child is not ClipNodeInstance clip)
                throw context.Error("the orientation warp child must be a clip node.");
            _clip = clip;
            _target = context.ValueNode(_def.TargetNodeIndex, _def.IsAngleOffset ? ValueInputKind.Number : ValueInputKind.Vector);

            foreach (AnimationEvent e in _clip.Clip.Events)
            {
                if (e is OrientationWarpEvent)
                {
                    _windowStart = e.StartTime;
                    _windowEnd = e.EndTime;
                    break;
                }
            }
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _needsWarp = true;
        }

        protected override void OnUpdate(GraphContext context)
        {
            if (_needsWarp)
            {
                _warped = ComputeWarp(context, _clip.NormalizedTime);
                _needsWarp = false;
            }

            base.OnUpdate(context);
            if (_warped is not null)
                RootMotionDelta = WarpedTrajectory.Delta(_warped, _clip.LastSpan);
        }

        private Transform3D[]? ComputeWarp(GraphContext context, float startTime)
        {
            if (!_clip.Clip.HasRootMotion)
                return null;
            RootMotion rootMotion = _clip.Clip.RootMotion!;
            ParameterValue value = _target.GetValue(context);
            return _def.IsAngleOffset
                ? RootMotionWarp.WarpOrientationByAngle(rootMotion, value.AsFloat(), _windowStart, _windowEnd, startTime)
                : RootMotionWarp.WarpOrientation(rootMotion, value.Vector, _windowStart, _windowEnd, startTime);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Target warp (rewrites a clip's root motion so the character reaches a desired displacement)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Wraps a clip and rewrites its root motion so the total travel matches a desired displacement by
/// the end of the clip. The goal is a Float3 displacement in character space measured from the clip's
/// first frame, or a world Target (converted with the character's world transform) that the character
/// should end on. Bone targets and unset targets disable the warp. The first
/// <see cref="TargetWarpEvent"/> limits warping to its time window and its rule picks the axes: WarpXY
/// warps the horizontal XZ plane, WarpZ the vertical Y, WarpXYZ both, RotationOnly disables translation
/// warping. Without an event the whole clip warps on all axes. A Vector goal is solved again when it
/// changes. A Target goal is solved once, on the first update or after a reseek.
/// </summary>
public sealed class TargetWarpDefinition : PoseNodeDefinition
{
    public TargetWarpDefinition(int clipChild, int targetNodeIndex)
    {
        ClipChild = clipChild;
        TargetNodeIndex = targetNodeIndex;
    }

    public int ClipChild { get; }
    public int TargetNodeIndex { get; }

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly TargetWarpDefinition _def;
        private ClipNodeInstance _clip = null!;
        private ValueNodeInstance _target = null!;
        private TargetWarpRule _rule = TargetWarpRule.WarpXYZ;
        private float _windowStart;
        private float _windowEnd = 1f;
        private Transform3D[]? _warped;
        private Float3 _solvedFor;
        private float _warpStartTime;
        private bool _needsSolve;

        public Instance(TargetWarpDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.ClipChild);
            if (Child is not ClipNodeInstance clip)
                throw context.Error("the target warp child must be a clip node.");
            _clip = clip;
            _target = context.ValueNode(_def.TargetNodeIndex, ValueInputKind.Vector | ValueInputKind.Target);

            foreach (AnimationEvent e in _clip.Clip.Events)
            {
                if (e is TargetWarpEvent tw)
                {
                    _rule = tw.Rule;
                    _windowStart = tw.StartTime;
                    _windowEnd = tw.EndTime;
                    break;
                }
            }
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _needsSolve = true;
            _warped = null;
        }

        protected override void OnUpdate(GraphContext context)
        {
            if (_clip.Clip.HasRootMotion)
                Solve(context, _clip.NormalizedTime);

            base.OnUpdate(context);
            if (_warped is not null)
                RootMotionDelta = WarpedTrajectory.Delta(_warped, _clip.LastSpan);
        }

        private void Solve(GraphContext context, float currentTime)
        {
            ParameterValue value = _target.GetValue(context);
            bool isTarget = value.Type == AnimationValueType.Target;
            if (isTarget && !_needsSolve)
                return;

            RootMotion rootMotion = _clip.Clip.RootMotion!;
            if (_needsSolve)
                _warpStartTime = currentTime;

            if (!TryGetDesired(value, context, rootMotion, out Float3 desired))
            {
                _warped = null;
                _needsSolve = false;
                return;
            }

            desired = ApplyRuleMask(desired, rootMotion.TotalDelta.position);
            if (!_needsSolve && _warped is not null && Float3.Distance(desired, _solvedFor) <= 1e-4f)
                return;
            _solvedFor = desired;

            // A goal that moves mid clip only rewrites the rest of the path, from where the warped path got to.
            Float3 goal = desired;
            if (!_needsSolve && _warped is not null)
            {
                _warpStartTime = currentTime;
                goal = desired - WarpedTrajectory.Sample(_warped, currentTime).position + rootMotion.SampleDelta(0f, currentTime).position;
            }

            _warped = _rule == TargetWarpRule.RotationOnly
                ? null
                : RootMotionWarp.WarpTrajectory(rootMotion, goal, _windowStart, _windowEnd, _warpStartTime);
            _needsSolve = false;
        }

        // The goal as a displacement from the clip's first frame, in character space.
        private bool TryGetDesired(in ParameterValue value, GraphContext context, RootMotion rootMotion, out Float3 desired)
        {
            desired = default;
            if (value.Type == AnimationValueType.Vector)
            {
                desired = value.Vector;
                return true;
            }
            if (value.Type != AnimationValueType.Target || value.Target.IsBoneTarget || !value.Target.TryGetTransform(Pose, out Transform3D resolved))
                return false;

            Float3 fromNow = context.WorldToCharacter(resolved.position);
            Transform3D covered = rootMotion.SampleDelta(0f, _warpStartTime);
            desired = TransformOps.Combine(covered, new Transform3D(fromNow, Quaternion.Identity, Float3.One)).position;
            return true;
        }

        private Float3 ApplyRuleMask(Float3 desired, Float3 original) => _rule switch
        {
            TargetWarpRule.WarpXY => new Float3(desired.X, original.Y, desired.Z),
            TargetWarpRule.WarpZ => new Float3(original.X, desired.Y, original.Z),
            TargetWarpRule.RotationOnly => original,
            _ => desired,
        };
    }
}
