using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Smooths over a pose discontinuity without playing two animations at once. When a jump happens it
/// measures the gap between the pose that was showing and the new one, along with how fast that gap was
/// already moving, then decays the gap to zero over the blend time with a curve that keeps both position
/// and velocity continuous. The result starts exactly on the old pose and lands exactly on the new one.
/// </summary>
/// <remarks>
/// Feed it every frame through <see cref="Apply"/>, which also keeps the two frames of history the next
/// <see cref="Begin"/> needs. It holds no per frame allocations.
/// </remarks>
public sealed class Inertializer
{
    /// <summary>One decaying offset: the quintic that takes it from its starting value and rate to zero.</summary>
    private struct Channel
    {
        private float _x0, _v0, _a0, _a, _b, _c, _t1, _t;

        public bool Active => _t1 > 0f && _t < _t1;

        public void Clear() => _t1 = 0f;

        /// <summary>Sets up the decay from an offset and its rate of change (negative means already closing).</summary>
        public void Begin(float x0, float v0, float duration)
        {
            _t = 0f;
            if (!float.IsFinite(x0) || MathF.Abs(x0) < 1e-7f || !(duration > 0f))
            {
                _t1 = 0f;
                return;
            }

            // Work with a positive offset so the "already closing" case is always a negative rate.
            float sign = MathF.Sign(x0);
            x0 *= sign;
            v0 = float.IsFinite(v0) ? v0 * sign : 0f;
            v0 = MathF.Min(v0, 0f);

            float t1 = duration;
            if (v0 < 0f)
                t1 = MathF.Min(t1, -5f * x0 / v0);

            float a0 = (-8f * v0 * t1 - 20f * x0) / (t1 * t1);
            float t1_2 = t1 * t1;
            _a = -(a0 * t1_2 + 6f * v0 * t1 + 12f * x0) / (2f * t1_2 * t1_2 * t1);
            _b = (3f * a0 * t1_2 + 16f * v0 * t1 + 30f * x0) / (2f * t1_2 * t1_2);
            _c = -(3f * a0 * t1_2 + 12f * v0 * t1 + 20f * x0) / (2f * t1_2 * t1);
            _a0 = a0;
            _x0 = x0 * sign;
            _v0 = v0 * sign;
            _a *= sign;
            _b *= sign;
            _c *= sign;
            _a0 *= sign;
            _t1 = t1;
        }

        /// <summary>Advances by a time step and returns the remaining offset.</summary>
        public float Advance(float deltaTime)
        {
            if (_t1 <= 0f)
                return 0f;

            _t += deltaTime;
            if (_t >= _t1)
            {
                _t1 = 0f;
                return 0f;
            }

            float t = _t;
            float t2 = t * t;
            float t3 = t2 * t;
            return _a * t3 * t2 + _b * t2 * t2 + _c * t3 + 0.5f * _a0 * t2 + _v0 * t + _x0;
        }
    }

    private struct BoneState
    {
        public Float3 RotationAxis;
        public Float3 PositionDirection;
        public Float3 ScaleDirection;
        public Channel Rotation;
        public Channel Position;
        public Channel Scale;
    }

    private readonly BoneState[] _bones;
    private readonly Channel[] _floats;
    private readonly Transform3D[] _previous;
    private readonly Transform3D[] _beforePrevious;

    // The poses asked for, before any offset. A jump is judged on these: judged on the output, the
    // offset still decaying from the last jump looks like a new one every frame.
    private readonly Transform3D[] _previousTarget;
    private readonly Transform3D[] _beforePreviousTarget;
    private readonly float[] _previousFloats;
    private readonly float[] _beforePreviousFloats;
    private float _lastDeltaTime;
    private int _history;

    public Inertializer(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        _bones = new BoneState[skeleton.BoneCount];
        _floats = new Channel[skeleton.FloatChannelCount];
        _previous = new Transform3D[skeleton.BoneCount];
        _beforePrevious = new Transform3D[skeleton.BoneCount];
        _previousTarget = new Transform3D[skeleton.BoneCount];
        _beforePreviousTarget = new Transform3D[skeleton.BoneCount];
        _previousFloats = new float[skeleton.FloatChannelCount];
        _beforePreviousFloats = new float[skeleton.FloatChannelCount];
    }

    /// <summary>True while an offset is still decaying.</summary>
    public bool IsBlending
    {
        get
        {
            foreach (BoneState bone in _bones)
                if (bone.Rotation.Active || bone.Position.Active || bone.Scale.Active)
                    return true;
            foreach (Channel channel in _floats)
                if (channel.Active)
                    return true;
            return false;
        }
    }

    /// <summary>Drops the history and any running blend.</summary>
    public void Reset()
    {
        for (int b = 0; b < _bones.Length; b++)
            _bones[b] = default;
        for (int c = 0; c < _floats.Length; c++)
            _floats[c].Clear();
        _history = 0;
        _lastDeltaTime = 0f;
    }

    /// <summary>
    /// True if a bone takes a step that looks like a discontinuity rather than motion: bigger than the
    /// given threshold, and several times bigger than the step it took the frame before. This is how a
    /// node without an explicit trigger spots a switch, while animation that simply moves fast, or
    /// speeds up, is left alone.
    /// </summary>
    public bool DetectJump(Pose target, float radians, float distance)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_history < 2)
            return false;

        for (int b = 0; b < _bones.Length; b++)
        {
            Transform3D transform = target.GetTransform(b);

            float step = AngleBetween(_previousTarget[b].rotation, transform.rotation);
            float lastStep = AngleBetween(_beforePreviousTarget[b].rotation, _previousTarget[b].rotation);
            if (step > radians && step > lastStep * JumpRatio + 1e-4f)
                return true;

            if (distance > 0f)
            {
                step = Float3.Length(_previousTarget[b].position - transform.position);
                lastStep = Float3.Length(_beforePreviousTarget[b].position - _previousTarget[b].position);
                if (step > distance && step > lastStep * JumpRatio + 1e-5f)
                    return true;
            }
        }
        return false;
    }

    /// <summary>How many times bigger than the previous step a step has to be to count as a jump.</summary>
    private const float JumpRatio = 3f;

    private static float AngleBetween(Quaternion a, Quaternion b)
        => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 0f, 1f));

    /// <summary>
    /// Starts a blend: the gap between the last output and <paramref name="target"/> decays to zero over
    /// <paramref name="blendSeconds"/>. Without two frames of history there is no velocity to preserve, so
    /// the call does nothing and the target shows through immediately.
    /// </summary>
    public void Begin(Pose target, float blendSeconds)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_history == 0 || !(blendSeconds > 0f))
            return;

        float dt = _lastDeltaTime > 1e-6f ? _lastDeltaTime : 1f / 60f;
        bool haveVelocity = _history > 1;

        for (int b = 0; b < _bones.Length; b++)
        {
            Transform3D targetTransform = target.GetTransform(b);
            ref BoneState state = ref _bones[b];

            // Rotation: the offset as a turn about one axis, held fixed for the whole blend.
            Quaternion offset = Quaternion.Normalize(_previous[b].rotation * Quaternion.Inverse(targetTransform.rotation));
            ToAxisAngle(offset, out state.RotationAxis, out float angle);
            float previousAngle = haveVelocity
                ? AngleAbout(Quaternion.Normalize(_beforePrevious[b].rotation * Quaternion.Inverse(targetTransform.rotation)), state.RotationAxis)
                : angle;
            state.Rotation.Begin(angle, (angle - previousAngle) / dt, blendSeconds);

            state.PositionDirection = BeginVector(
                _previous[b].position - targetTransform.position,
                haveVelocity ? _beforePrevious[b].position - targetTransform.position : _previous[b].position - targetTransform.position,
                dt, blendSeconds, ref state.Position);

            state.ScaleDirection = BeginVector(
                _previous[b].scale - targetTransform.scale,
                haveVelocity ? _beforePrevious[b].scale - targetTransform.scale : _previous[b].scale - targetTransform.scale,
                dt, blendSeconds, ref state.Scale);
        }

        for (int c = 0; c < _floats.Length && c < target.FloatChannelCount; c++)
        {
            float offset = _previousFloats[c] - target.GetFloat(c);
            float before = haveVelocity ? _beforePreviousFloats[c] - target.GetFloat(c) : offset;
            _floats[c].Begin(offset, (offset - before) / dt, blendSeconds);
        }
    }

    /// <summary>
    /// Writes <paramref name="target"/> into <paramref name="result"/> with any running offset still
    /// added on, and records the frame so a later <see cref="Begin"/> knows where the pose came from.
    /// The two poses may be the same object.
    /// </summary>
    public void Apply(Pose target, Pose result, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(result);
        if (target.BoneCount != _bones.Length || result.BoneCount != _bones.Length)
            throw new ArgumentException("Inertialization poses must match the skeleton bone count.");

        float step = float.IsFinite(deltaTime) && deltaTime > 0f ? deltaTime : 0f;

        for (int b = 0; b < _bones.Length; b++)
        {
            ref BoneState state = ref _bones[b];
            Transform3D transform = target.GetTransform(b);
            _beforePreviousTarget[b] = _previousTarget[b];
            _previousTarget[b] = transform;

            float angle = state.Rotation.Advance(step);
            if (angle != 0f)
                transform = new Transform3D(transform.position, Quaternion.Normalize(Quaternion.AxisAngle(state.RotationAxis, angle) * transform.rotation), transform.scale);

            float position = state.Position.Advance(step);
            float scale = state.Scale.Advance(step);
            if (position != 0f || scale != 0f)
                transform = new Transform3D(transform.position + state.PositionDirection * position, transform.rotation, transform.scale + state.ScaleDirection * scale);

            _beforePrevious[b] = _previous[b];
            _previous[b] = transform;
            result.WriteLocal(b, transform);
        }

        for (int c = 0; c < _floats.Length && c < target.FloatChannelCount; c++)
        {
            float value = target.GetFloat(c) + _floats[c].Advance(step);
            _beforePreviousFloats[c] = _previousFloats[c];
            _previousFloats[c] = value;
            result.WriteFloat(c, value);
        }

        result.FinishWrite(target.State == PoseState.Unset ? PoseState.Pose : target.State);
        _lastDeltaTime = step;
        _history = _history < 2 ? _history + 1 : 2;
    }

    private static Float3 BeginVector(Float3 offset, Float3 before, float dt, float blendSeconds, ref Channel channel)
    {
        float length = Float3.Length(offset);
        if (length < 1e-7f)
        {
            channel.Begin(0f, 0f, blendSeconds);
            return Float3.Zero;
        }

        Float3 direction = offset / length;
        channel.Begin(length, (length - Float3.Dot(before, direction)) / dt, blendSeconds);
        return direction;
    }

    private static void ToAxisAngle(Quaternion q, out Float3 axis, out float angle)
    {
        if (q.W < 0f)
            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);

        var vector = new Float3(q.X, q.Y, q.Z);
        float length = Float3.Length(vector);
        if (length < 1e-7f)
        {
            axis = new Float3(0f, 1f, 0f);
            angle = 0f;
            return;
        }

        axis = vector / length;
        angle = 2f * MathF.Atan2(length, Math.Clamp(q.W, -1f, 1f));
    }

    /// <summary>The turn of a rotation measured about one axis, which may be negative.</summary>
    private static float AngleAbout(Quaternion q, Float3 axis)
    {
        if (q.W < 0f)
            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        float along = Float3.Dot(new Float3(q.X, q.Y, q.Z), axis);
        return 2f * MathF.Atan2(along, Math.Clamp(q.W, -1f, 1f));
    }
}
