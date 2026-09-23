using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>What a <see cref="CharacterMotionDefinition"/> reads off the character's own movement.</summary>
public enum CharacterMotionValue : byte
{
    /// <summary>Speed across the ground, in units per second.</summary>
    Speed,
    /// <summary>Speed along the way the character faces. Negative when backing up.</summary>
    ForwardSpeed,
    /// <summary>Speed to the character's right. Negative when moving left.</summary>
    SideSpeed,
    VerticalSpeed,
    /// <summary>The heading of travel against the facing, in degrees, positive to the right. Zero when still.</summary>
    Direction,
    /// <summary>How fast the character turns about the up axis, in degrees per second, positive to the right.</summary>
    TurnRate,
    /// <summary>The whole velocity, in character space.</summary>
    Velocity,
}

/// <summary>
/// How the character itself is moving, read from the world transform the graph is handed each update,
/// so a graph can blend by speed or lean into a turn without the game feeding those in.
/// </summary>
public sealed class CharacterMotionDefinition : ValueNodeDefinition
{
    public CharacterMotionDefinition(CharacterMotionValue value) => Value = value;

    public CharacterMotionValue Value { get; }

    /// <summary>How long the reading takes to settle halfway to a change, in seconds. Zero follows every frame.</summary>
    public float HalfLife { get; init; } = 0.1f;

    public override AnimationValueType ValueType => Value == CharacterMotionValue.Velocity ? AnimationValueType.Vector : AnimationValueType.Float;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly CharacterMotionDefinition _def;
        private Transform3D _last;
        private double _lastTime;
        private bool _started;
        private Float3 _velocity;
        private float _turnRate;

        public Instance(CharacterMotionDefinition def) => _def = def;

        protected override void OnInitialize(GraphContext context)
        {
            _started = false;
            _velocity = Float3.Zero;
            _turnRate = 0f;
        }

        protected override ParameterValue Compute(GraphContext context)
        {
            Transform3D now = context.WorldTransform;
            if (!_started)
            {
                _last = now;
                _lastTime = context.Time;
                _started = true;
                return Read();
            }

            float elapsed = (float)(context.Time - _lastTime);
            if (elapsed > 1e-6f)
            {
                Float3 travel = (now.position - _last.position) / elapsed;
                Float3 velocity = Quaternion.Inverse(now.rotation) * travel;
                float turn = Maths.Rad2Deg * WrapRadians(Yaw(now.rotation) - Yaw(_last.rotation)) / elapsed;

                float follow = _def.HalfLife > 0f ? 1f - MathF.Pow(0.5f, elapsed / _def.HalfLife) : 1f;
                _velocity += (velocity - _velocity) * follow;
                _turnRate += (turn - _turnRate) * follow;

                _last = now;
                _lastTime = context.Time;
            }
            return Read();
        }

        private ParameterValue Read() => _def.Value switch
        {
            CharacterMotionValue.Speed => ParameterValue.FromFloat(MathF.Sqrt(_velocity.X * _velocity.X + _velocity.Z * _velocity.Z)),
            CharacterMotionValue.ForwardSpeed => ParameterValue.FromFloat(_velocity.Z),
            CharacterMotionValue.SideSpeed => ParameterValue.FromFloat(_velocity.X),
            CharacterMotionValue.VerticalSpeed => ParameterValue.FromFloat(_velocity.Y),
            CharacterMotionValue.Direction => ParameterValue.FromFloat(Heading(_velocity)),
            CharacterMotionValue.TurnRate => ParameterValue.FromFloat(_turnRate),
            _ => ParameterValue.FromVector(_velocity),
        };

        // A heading is noise while barely moving, so standing still reads as straight ahead.
        private static float Heading(Float3 velocity)
            => velocity.X * velocity.X + velocity.Z * velocity.Z < 1e-4f ? 0f : Maths.Rad2Deg * MathF.Atan2(velocity.X, velocity.Z);

        private static float Yaw(Quaternion rotation)
        {
            Float3 forward = rotation * Float3.UnitZ;
            return MathF.Atan2(forward.X, forward.Z);
        }

        private static float WrapRadians(float angle)
        {
            while (angle > MathF.PI) angle -= 2f * MathF.PI;
            while (angle < -MathF.PI) angle += 2f * MathF.PI;
            return angle;
        }
    }
}
