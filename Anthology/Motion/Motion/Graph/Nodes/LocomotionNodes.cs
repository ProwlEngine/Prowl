using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

// ---- Foot lock ---------------------------------------------------------------------------------

/// <summary>
/// Pins a foot in world space while it is planted, so it stops sliding when the character's speed and
/// the clip's stride disagree. Drive the lock from a foot event condition on the playing clip, or from
/// any other bool. When the lock ends the leg eases back onto the animation over a short release.
/// </summary>
public sealed class FootLockDefinition : PoseNodeDefinition
{
    public FootLockDefinition(int child, StringID upper, StringID mid, StringID end, int lockNodeIndex)
    {
        Child = child;
        Upper = upper;
        Mid = mid;
        End = end;
        LockNodeIndex = lockNodeIndex;
    }

    public int Child { get; }

    /// <summary>The three bones of the leg, from the hip down to the foot.</summary>
    public StringID Upper { get; }

    public StringID Mid { get; }

    public StringID End { get; }

    /// <summary>Bool value node: the foot is pinned while it reads true.</summary>
    public int LockNodeIndex { get; }

    /// <summary>How long the leg takes to give up the pin once the lock ends, in seconds.</summary>
    public float ReleaseSeconds { get; set; } = 0.15f;

    /// <summary>Pinning also holds the foot's orientation, not just its position.</summary>
    public bool LockRotation { get; set; } = true;

    /// <summary>
    /// How far the character may walk away from a pinned foot before it is let go, so a lock left on
    /// by mistake cannot stretch the leg across the level.
    /// </summary>
    public float BreakDistance { get; set; } = 1f;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly FootLockDefinition _def;
        private ValueNodeInstance _lock = null!;
        private int _upper, _mid, _end;
        private Transform3D _pinned;
        private bool _locked;
        private float _weight;

        public Instance(FootLockDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _upper = context.Skeleton.GetBoneIndex(_def.Upper);
            _mid = context.Skeleton.GetBoneIndex(_def.Mid);
            _end = context.Skeleton.GetBoneIndex(_def.End);
            _lock = context.ValueNode(_def.LockNodeIndex, ValueInputKind.Number);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            base.OnInitialize(context, initialTime);
            _locked = false;
            _weight = 0f;
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            if (_upper == Skeleton.InvalidIndex || _mid == Skeleton.InvalidIndex || _end == Skeleton.InvalidIndex)
                return;

            Transform3D world = context.WorldTransform;
            bool wanted = _lock.GetValue(context).AsBool();

            if (wanted && !_locked)
            {
                Transform3D foot = Pose.GetModelSpaceTransform(_end);
                _pinned = new Transform3D(ToWorld(world, foot.position), world.rotation * foot.rotation, foot.scale);
                _locked = true;
            }

            Float3 goal = context.WorldToCharacter(_pinned.position);
            if (_locked && _def.BreakDistance > 0f && Float3.Length(goal - Pose.GetModelSpaceTransform(_end).position) > _def.BreakDistance)
                _locked = false;

            if (!wanted)
                _locked = false;

            float target = _locked ? 1f : 0f;
            _weight = Approach(_weight, target, context.DeltaTime, _def.ReleaseSeconds);
            if (!(_weight > 0f))
                return;

            TwoBoneIK.Solve(Pose, _upper, _mid, _end, goal, _weight);

            if (_def.LockRotation)
            {
                Quaternion pinned = Quaternion.Normalize(Quaternion.Inverse(world.rotation) * _pinned.rotation);
                BoneWriter.SetModelRotation(Pose, _end, Quaternion.Slerp(Pose.GetModelSpaceTransform(_end).rotation, pinned, _weight));
            }
        }

        private static float Approach(float value, float target, float deltaTime, float seconds)
        {
            if (!(seconds > 0f))
                return target;
            if (!(MathF.Abs(deltaTime) > 0f))
                return value;
            float step = MathF.Abs(deltaTime) / seconds;
            return value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
        }

        private static Float3 ToWorld(in Transform3D world, Float3 point) => world.position + world.rotation * (point * world.scale);
    }
}

// ---- Stride warp -------------------------------------------------------------------------------

/// <summary>
/// Matches a clip's travel to the speed the game actually wants, by bending the clip's playback rate
/// and its root motion together. A walk played 20 percent faster covers 20 percent more ground per
/// second, so the feet keep up with the character instead of skating.
/// </summary>
/// <remarks>
/// The clip's own speed is measured from its root motion, or given through <see cref="NaturalSpeed"/>
/// when the clip has none. A clip that does not travel cannot be warped, so it plays untouched.
/// </remarks>
public sealed class StrideWarpDefinition : PoseNodeDefinition
{
    public StrideWarpDefinition(int child, int desiredSpeedNodeIndex)
    {
        Child = child;
        DesiredSpeedNodeIndex = desiredSpeedNodeIndex;
    }

    public int Child { get; }

    /// <summary>Float value node giving the speed the character should travel at, in units per second.</summary>
    public int DesiredSpeedNodeIndex { get; }

    /// <summary>The clip's own travel speed, or 0 to measure it from the clip's root motion.</summary>
    public FloatInput NaturalSpeed { get; set; }

    /// <summary>Limits on how far the playback rate may be bent.</summary>
    public FloatInput MinScale { get; set; } = 0.5f;

    public FloatInput MaxScale { get; set; } = 2f;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly StrideWarpDefinition _def;
        private ValueNodeInstance _desired = null!;
        private BoundFloat _natural, _minScale, _maxScale;
        private float _scale = 1f;

        public Instance(StrideWarpDefinition def) => _def = def;

        /// <summary>The rate the clip is currently being played at.</summary>
        public float Scale => _scale;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _desired = context.ValueNode(_def.DesiredSpeedNodeIndex, ValueInputKind.Number);
            _natural = BoundFloat.Bind(context, _def.NaturalSpeed);
            _minScale = BoundFloat.Bind(context, _def.MinScale);
            _maxScale = BoundFloat.Bind(context, _def.MaxScale);
        }

        protected override void OnUpdate(GraphContext context)
        {
            _scale = ScaleFor(context);

            if (context.SyncRange.HasValue || _scale == 1f)
            {
                Child.Update(context);
            }
            else
            {
                float saved = context.DeltaTime;
                context.DeltaTime = saved * _scale;
                Child.Update(context);
                context.DeltaTime = saved;
            }

            CopyResultFrom(Child);
            Duration = _scale > 1e-6f ? Child.Duration / _scale : Child.Duration;
        }

        private float ScaleFor(GraphContext context)
        {
            float desired = _desired.GetValue(context).AsFloat();
            if (!float.IsFinite(desired) || desired < 0f)
                return 1f;

            float asked = _natural.Get(context);
            float natural = asked > 0f ? asked : NaturalFromChild();
            if (!(natural > 1e-4f))
                return 1f;

            // Either bound can be driven, so they may arrive crossed, which a clamp would throw on.
            float min = _minScale.Get(context), max = _maxScale.Get(context);
            return Math.Clamp(desired / natural, MathF.Min(min, max), MathF.Max(min, max));
        }

        private float NaturalFromChild() => Child is ClipNodeInstance clip ? clip.Clip.AverageLinearSpeed : 0f;
    }
}

// ---- Root motion filter ------------------------------------------------------------------------

/// <summary>Which parts of a root motion delta survive a <see cref="RootMotionFilterDefinition"/>.</summary>
[Flags]
public enum RootMotionChannels : byte
{
    None = 0,
    X = 1,
    Y = 2,
    Z = 4,
    Rotation = 8,
    Horizontal = X | Z,
    All = X | Y | Z | Rotation,
}

/// <summary>
/// Keeps only part of a clip's root motion: strip the turn from a clip that drifts, hold a character at
/// a fixed height, or drop translation entirely to play a travelling clip on the spot.
/// </summary>
public sealed class RootMotionFilterDefinition : PoseNodeDefinition
{
    public RootMotionFilterDefinition(int child, RootMotionChannels keep)
    {
        Child = child;
        Keep = keep;
    }

    public int Child { get; }

    public RootMotionChannels Keep { get; }

    /// <summary>Scales whatever survives the filter, so motion can be damped rather than removed.</summary>
    public FloatInput Scale { get; set; } = 1f;

    /// <summary>Scales the travel that survives, on top of <see cref="Scale"/>.</summary>
    public FloatInput TravelScale { get; set; } = 1f;

    /// <summary>Scales the turn that survives, on top of <see cref="Scale"/>.</summary>
    public FloatInput TurnScale { get; set; } = 1f;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PassthroughPoseNodeInstance
    {
        private readonly RootMotionFilterDefinition _def;
        private BoundFloat _scale, _travel, _turn;

        public Instance(RootMotionFilterDefinition def) => _def = def;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, _def.Child);
            _scale = BoundFloat.Bind(context, _def.Scale);
            _travel = BoundFloat.Bind(context, _def.TravelScale);
            _turn = BoundFloat.Bind(context, _def.TurnScale);
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);

            float scale = _scale.Get(context);
            float travel = scale * _travel.Get(context);
            float turn = scale * _turn.Get(context);
            Transform3D delta = RootMotionDelta;
            var position = new Float3(
                (_def.Keep & RootMotionChannels.X) != 0 ? delta.position.X * travel : 0f,
                (_def.Keep & RootMotionChannels.Y) != 0 ? delta.position.Y * travel : 0f,
                (_def.Keep & RootMotionChannels.Z) != 0 ? delta.position.Z * travel : 0f);

            Quaternion rotation = (_def.Keep & RootMotionChannels.Rotation) != 0
                ? (turn == 1f ? delta.rotation : Quaternion.Slerp(Quaternion.Identity, delta.rotation, turn))
                : Quaternion.Identity;

            RootMotionDelta = new Transform3D(position, rotation, delta.scale);
        }
    }
}
