// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Prowl.Vector;

/// <summary>
/// A keyframed curve with one to four components per key.
/// </summary>
/// <remarks>
/// <para>
/// Keys are always sorted by time and stored in flat float arrays, so evaluation walks contiguous
/// memory and a curve imported from a glTF sampler can adopt the accessor buffers without copying.
/// <see cref="Keyframe"/> is a view over that storage: reading an index materialises a copy, so
/// edits must be written back through the indexer or <see cref="MoveKey"/>.
/// </para>
/// <para>
/// Interpolation is per key and governs the segment leaving it, which is what lets a single curve
/// mix stepped and smooth sections. Tangents are slopes in value units per second (the glTF and
/// Unity convention), so retiming keys does not change the shape of the handles.
/// </para>
/// </remarks>
[Serializable]
public sealed class AnimationCurve : IReadOnlyList<Keyframe>
{
    private const int MinimumCapacity = 4;

    // [DataMember] marks these for reflection based serializers such as Prowl.Echo. It is the BCL
    // attribute, so Vector takes no dependency on any serializer to be round-trippable.
    [DataMember] private int _dimension;
    [DataMember] private int _count;
    [DataMember] private float[] _times;
    [DataMember] private float[] _values;
    [DataMember] private float[]? _inTangents;
    [DataMember] private float[]? _outTangents;
    [DataMember] private CurveInterpolation[] _modes;

    // Bumped by every mutation so an enumerator can notice the curve changed under it.
    private int _version;

    /// <summary>Behaviour for times before the first key.</summary>
    public CurveWrapMode PreWrap = CurveWrapMode.Clamp;

    /// <summary>Behaviour for times after the last key.</summary>
    public CurveWrapMode PostWrap = CurveWrapMode.Clamp;

    /// <summary>Components stored per key: 1 (scalar), 2, 3 (vector) or 4 (quaternion or colour).</summary>
    public int Dimension => _dimension;

    /// <summary>Number of keys on the curve.</summary>
    public int Count => _count;

    /// <summary>Time of the first key, or zero when the curve is empty.</summary>
    public float StartTime => _count > 0 ? _times[0] : 0f;

    /// <summary>Time of the last key, or zero when the curve is empty.</summary>
    public float EndTime => _count > 0 ? _times[_count - 1] : 0f;

    /// <summary>Time span covered by the keys.</summary>
    public float Duration => EndTime - StartTime;

    /// <summary>Creates an empty scalar curve.</summary>
    public AnimationCurve() : this(1) { }

    /// <summary>Creates an empty curve with the given component count.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="dimension"/> is not in the range 1 to 4.</exception>
    public AnimationCurve(int dimension)
    {
        if (dimension < 1 || dimension > 4)
            throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension must be between 1 and 4.");

        _dimension = dimension;
        _times = Array.Empty<float>();
        _values = Array.Empty<float>();
        _modes = Array.Empty<CurveInterpolation>();
    }

    /// <summary>Creates a scalar curve from the given keys.</summary>
    public AnimationCurve(params Keyframe[] keys) : this(1, keys) { }

    /// <summary>Creates a curve with the given component count from the given keys.</summary>
    public AnimationCurve(int dimension, params Keyframe[] keys) : this(dimension, (IEnumerable<Keyframe>)keys) { }

    /// <summary>Creates a curve with the given component count from the given keys.</summary>
    public AnimationCurve(int dimension, IEnumerable<Keyframe> keys) : this(dimension)
    {
        if (keys is null) throw new ArgumentNullException(nameof(keys));
        foreach (Keyframe key in keys)
            AddKey(key);
    }

    /// <summary>Gets or sets the key at <paramref name="index"/>. Setting behaves like <see cref="MoveKey"/> and may reorder the curve.</summary>
    public Keyframe this[int index]
    {
        get
        {
            ThrowIfOutOfRange(index);
            return ReadKey(index);
        }
        set => MoveKey(index, value);
    }

    #region Factories

    /// <summary>Creates a curve holding a single value for all time.</summary>
    public static AnimationCurve Constant(float value) => new AnimationCurve(new Keyframe(0f, value) { Interpolation = CurveInterpolation.Step });

    /// <summary>Creates a curve holding a single value across the given range.</summary>
    public static AnimationCurve Constant(float startTime, float endTime, float value) =>
        new AnimationCurve(
            new Keyframe(startTime, value) { Interpolation = CurveInterpolation.Step },
            new Keyframe(endTime, value) { Interpolation = CurveInterpolation.Step });

    /// <summary>Creates a straight ramp between two values.</summary>
    public static AnimationCurve Linear(float startTime, float startValue, float endTime, float endValue) =>
        new AnimationCurve(new Keyframe(startTime, startValue), new Keyframe(endTime, endValue));

    /// <summary>Creates a smoothstep-shaped ramp between two values, flat at both ends.</summary>
    public static AnimationCurve EaseInOut(float startTime, float startValue, float endTime, float endValue) =>
        new AnimationCurve(
            new Keyframe(startTime, startValue, 0f, 0f),
            new Keyframe(endTime, endValue, 0f, 0f));

    /// <summary>
    /// Creates a curve over flat key data where every key uses the same interpolation.
    /// The arrays are adopted rather than copied, so they must not be mutated afterwards.
    /// </summary>
    /// <param name="dimension">Components per key.</param>
    /// <param name="times">Non-decreasing key times, one per key.</param>
    /// <param name="values">Key values laid out as <c>[key0_components, key1_components, ...]</c>.</param>
    /// <param name="interpolation">Interpolation applied to every key.</param>
    public static AnimationCurve FromPacked(int dimension, float[] times, float[] values, CurveInterpolation interpolation) =>
        FromPacked(dimension, times, values, null, null, interpolation);

    /// <summary>
    /// Creates a curve over flat key data with explicit tangents.
    /// The arrays are adopted rather than copied, so they must not be mutated afterwards.
    /// </summary>
    /// <param name="dimension">Components per key.</param>
    /// <param name="times">Non-decreasing key times, one per key.</param>
    /// <param name="values">Key values laid out as <c>[key0_components, key1_components, ...]</c>.</param>
    /// <param name="inTangents">In tangents in the same layout as <paramref name="values"/>, or null for zero.</param>
    /// <param name="outTangents">Out tangents in the same layout as <paramref name="values"/>, or null for zero.</param>
    /// <param name="interpolation">Interpolation applied to every key.</param>
    public static AnimationCurve FromPacked(int dimension, float[] times, float[] values, float[]? inTangents, float[]? outTangents, CurveInterpolation interpolation)
    {
        if (times is null) throw new ArgumentNullException(nameof(times));
        if (values is null) throw new ArgumentNullException(nameof(values));

        var curve = new AnimationCurve(dimension);
        int count = times.Length;
        if (values.Length != count * dimension)
            throw new ArgumentException($"Expected {count * dimension} values for {count} keys of dimension {dimension}, got {values.Length}.", nameof(values));
        if (inTangents != null && inTangents.Length != values.Length)
            throw new ArgumentException("In tangents must have the same length as values.", nameof(inTangents));
        if (outTangents != null && outTangents.Length != values.Length)
            throw new ArgumentException("Out tangents must have the same length as values.", nameof(outTangents));
        ValidateSorted(times, count);

        curve._times = times;
        curve._values = values;
        curve._inTangents = inTangents;
        curve._outTangents = outTangents;
        curve._modes = new CurveInterpolation[count];
        curve._count = count;
        for (int i = 0; i < count; i++)
            curve._modes[i] = interpolation;
        return curve;
    }

    /// <summary>
    /// Creates a cubic curve from a glTF <c>CUBICSPLINE</c> sampler, whose output is interleaved as
    /// <c>[key0_inTangent, key0_value, key0_outTangent, key1_inTangent, ...]</c>.
    /// </summary>
    /// <param name="dimension">Components per key.</param>
    /// <param name="times">Non-decreasing key times, one per key.</param>
    /// <param name="interleaved">Sampler output containing <c>3 * dimension</c> floats per key.</param>
    public static AnimationCurve FromGltfCubicSpline(int dimension, float[] times, float[] interleaved)
    {
        if (times is null) throw new ArgumentNullException(nameof(times));
        if (interleaved is null) throw new ArgumentNullException(nameof(interleaved));

        int count = times.Length;
        if (interleaved.Length != count * dimension * 3)
            throw new ArgumentException($"Expected {count * dimension * 3} values for {count} cubic keys of dimension {dimension}, got {interleaved.Length}.", nameof(interleaved));

        float[] values = new float[count * dimension];
        float[] inTangents = new float[count * dimension];
        float[] outTangents = new float[count * dimension];
        for (int k = 0; k < count; k++)
        {
            int src = k * dimension * 3;
            int dst = k * dimension;
            for (int c = 0; c < dimension; c++)
            {
                inTangents[dst + c] = interleaved[src + c];
                values[dst + c] = interleaved[src + dimension + c];
                outTangents[dst + c] = interleaved[src + dimension * 2 + c];
            }
        }
        return FromPacked(dimension, times, values, inTangents, outTangents, CurveInterpolation.CubicSpline);
    }

    /// <summary>Creates an independent copy of this curve.</summary>
    public AnimationCurve Clone()
    {
        var clone = new AnimationCurve(_dimension)
        {
            PreWrap = PreWrap,
            PostWrap = PostWrap,
            _count = _count,
            _times = Copy(_times, _count),
            _values = Copy(_values, _count * _dimension),
            _inTangents = _inTangents is null ? null : Copy(_inTangents, _count * _dimension),
            _outTangents = _outTangents is null ? null : Copy(_outTangents, _count * _dimension),
            _modes = new CurveInterpolation[_count]
        };
        Array.Copy(_modes, clone._modes, _count);
        return clone;
    }

    private static float[] Copy(float[] source, int length)
    {
        float[] result = new float[length];
        Array.Copy(source, result, length);
        return result;
    }

    #endregion

    #region Editing

    /// <summary>Adds a key, keeping the curve sorted, and returns its index.</summary>
    public int AddKey(float time, float value) => AddKey(new Keyframe(time, value));

    /// <summary>Adds a two-component key, keeping the curve sorted, and returns its index.</summary>
    public int AddKey(float time, Float2 value) => AddKey(new Keyframe(time, value));

    /// <summary>Adds a three-component key, keeping the curve sorted, and returns its index.</summary>
    public int AddKey(float time, Float3 value) => AddKey(new Keyframe(time, value));

    /// <summary>Adds a four-component key, keeping the curve sorted, and returns its index.</summary>
    public int AddKey(float time, Float4 value) => AddKey(new Keyframe(time, value));

    /// <summary>Adds a key, keeping the curve sorted, and returns its index. Keys sharing a time are appended after the existing ones.</summary>
    public int AddKey(in Keyframe key)
    {
        int index = UpperBound(key.Time);
        EnsureCapacity(_count + 1);

        int tail = _count - index;
        if (tail > 0)
        {
            Array.Copy(_times, index, _times, index + 1, tail);
            Array.Copy(_values, index * _dimension, _values, (index + 1) * _dimension, tail * _dimension);
            Array.Copy(_modes, index, _modes, index + 1, tail);
            if (_inTangents != null) Array.Copy(_inTangents, index * _dimension, _inTangents, (index + 1) * _dimension, tail * _dimension);
            if (_outTangents != null) Array.Copy(_outTangents, index * _dimension, _outTangents, (index + 1) * _dimension, tail * _dimension);
        }

        _count++;
        WriteKey(index, key);
        return index;
    }

    /// <summary>Removes the key at <paramref name="index"/>.</summary>
    public void RemoveKey(int index)
    {
        ThrowIfOutOfRange(index);

        int tail = _count - index - 1;
        if (tail > 0)
        {
            Array.Copy(_times, index + 1, _times, index, tail);
            Array.Copy(_values, (index + 1) * _dimension, _values, index * _dimension, tail * _dimension);
            Array.Copy(_modes, index + 1, _modes, index, tail);
            if (_inTangents != null) Array.Copy(_inTangents, (index + 1) * _dimension, _inTangents, index * _dimension, tail * _dimension);
            if (_outTangents != null) Array.Copy(_outTangents, (index + 1) * _dimension, _outTangents, index * _dimension, tail * _dimension);
        }
        _count--;
        _version++;
    }

    /// <summary>Replaces the key at <paramref name="index"/> and returns its index after any reordering.</summary>
    public int MoveKey(int index, in Keyframe key)
    {
        ThrowIfOutOfRange(index);

        if (_times[index] == key.Time)
        {
            WriteKey(index, key);
            return index;
        }

        RemoveKey(index);
        return AddKey(key);
    }

    /// <summary>Removes every key and any tangent storage. The component count is unchanged.</summary>
    public void Clear()
    {
        _count = 0;
        _inTangents = null;
        _outTangents = null;
        _version++;
    }

    /// <summary>Copies the keys into a new array.</summary>
    public Keyframe[] GetKeys()
    {
        var keys = new Keyframe[_count];
        for (int i = 0; i < _count; i++)
            keys[i] = ReadKey(i);
        return keys;
    }

    /// <summary>Replaces every key on the curve. The supplied keys do not need to be sorted.</summary>
    public void SetKeys(IEnumerable<Keyframe> keys)
    {
        if (keys is null) throw new ArgumentNullException(nameof(keys));
        Clear();
        foreach (Keyframe key in keys)
            AddKey(key);
    }

    /// <summary>
    /// Releases the spare capacity left by <see cref="AddKey(in Keyframe)"/>. Worth calling before
    /// serializing a curve that was built key by key, since the spare slots are stored too.
    /// </summary>
    public void TrimExcess()
    {
        if (_times.Length == _count) return;

        Array.Resize(ref _times, _count);
        Array.Resize(ref _values, _count * _dimension);
        Array.Resize(ref _modes, _count);
        if (_inTangents != null) Array.Resize(ref _inTangents, _count * _dimension);
        if (_outTangents != null) Array.Resize(ref _outTangents, _count * _dimension);
        _version++;
    }

    /// <summary>Key times in ascending order.</summary>
    public ReadOnlySpan<float> GetTimes() => new ReadOnlySpan<float>(_times, 0, _count);

    /// <summary>Key values laid out as <c>[key0_components, key1_components, ...]</c>.</summary>
    public ReadOnlySpan<float> GetValues() => new ReadOnlySpan<float>(_values, 0, _count * _dimension);

    /// <summary>In tangents in the same layout as <see cref="GetValues"/>, or an empty span when the curve has none.</summary>
    public ReadOnlySpan<float> GetInTangents() => _inTangents is null ? default : new ReadOnlySpan<float>(_inTangents, 0, _count * _dimension);

    /// <summary>Out tangents in the same layout as <see cref="GetValues"/>, or an empty span when the curve has none.</summary>
    public ReadOnlySpan<float> GetOutTangents() => _outTangents is null ? default : new ReadOnlySpan<float>(_outTangents, 0, _count * _dimension);

    /// <summary>Interpolation mode of each key, one per key.</summary>
    public ReadOnlySpan<CurveInterpolation> GetInterpolations() => new ReadOnlySpan<CurveInterpolation>(_modes, 0, _count);

    #endregion

    #region Tangents

    /// <summary>Recomputes every tangent and switches every key to <see cref="CurveInterpolation.CubicSpline"/>.</summary>
    public void SmoothTangents(CurveTangentMode mode) => SmoothTangents(mode, mode);

    /// <summary>Recomputes every tangent and switches every key to <see cref="CurveInterpolation.CubicSpline"/>.</summary>
    public void SmoothTangents(CurveTangentMode inMode, CurveTangentMode outMode)
    {
        for (int i = 0; i < _count; i++)
            SmoothTangent(i, inMode, outMode);
    }

    /// <summary>Recomputes the tangents of one key and switches it to <see cref="CurveInterpolation.CubicSpline"/>.</summary>
    public void SmoothTangent(int index, CurveTangentMode mode) => SmoothTangent(index, mode, mode);

    /// <summary>Recomputes the tangents of one key and switches it to <see cref="CurveInterpolation.CubicSpline"/>.</summary>
    public void SmoothTangent(int index, CurveTangentMode inMode, CurveTangentMode outMode)
    {
        ThrowIfOutOfRange(index);
        EnsureTangents();

        bool interior = index > 0 && index < _count - 1;
        int prev = index > 0 ? index - 1 : index;
        int next = index < _count - 1 ? index + 1 : index;
        float dtPrev = _times[index] - _times[prev];
        float dtNext = _times[next] - _times[index];
        float dtSpan = _times[next] - _times[prev];

        for (int c = 0; c < _dimension; c++)
        {
            int offset = index * _dimension + c;
            float v = _values[offset];
            float v0 = _values[prev * _dimension + c];
            float v1 = _values[next * _dimension + c];

            float slopePrev = dtPrev > 0f ? (v - v0) / dtPrev : float.NaN;
            float slopeNext = dtNext > 0f ? (v1 - v) / dtNext : float.NaN;
            float slopeAuto = dtSpan > 0f ? (v1 - v0) / dtSpan : 0f;
            bool extremum = interior && ((v >= v0 && v >= v1) || (v <= v0 && v <= v1));

            _inTangents![offset] = Tangent(inMode, slopePrev, slopeNext, slopeAuto, extremum);
            _outTangents![offset] = Tangent(outMode, slopeNext, slopePrev, slopeAuto, extremum);
        }

        _modes[index] = CurveInterpolation.CubicSpline;
        _version++;
    }

    private static float Tangent(CurveTangentMode mode, float ownSide, float otherSide, float auto, bool extremum)
    {
        switch (mode)
        {
            case CurveTangentMode.Flat:
                return 0f;
            case CurveTangentMode.Linear:
                if (!float.IsNaN(ownSide)) return ownSide;
                return float.IsNaN(otherSide) ? 0f : otherSide;
            case CurveTangentMode.ClampedAuto:
                return extremum ? 0f : auto;
            default:
                return auto;
        }
    }

    private void EnsureTangents()
    {
        int length = _times.Length * _dimension;
        _inTangents ??= new float[length];
        _outTangents ??= new float[length];
    }

    #endregion

    #region Evaluation

    /// <summary>Evaluates the first component of the curve.</summary>
    public float Evaluate(float time) => EvaluateComponent(0, time);

    /// <summary>Evaluates a single component of the curve.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="component"/> is not a component of this curve.</exception>
    public float EvaluateComponent(int component, float time)
    {
        if (component < 0 || component >= _dimension)
            throw new ArgumentOutOfRangeException(nameof(component), component, $"Curve has {_dimension} component(s).");
        if (_count == 0) return 0f;
        if (_count == 1) return _values[component];

        if (TryExtrapolate(component, time, out float extrapolated))
            return extrapolated;

        ResolveTime(time, out float mapped, out int cycle);
        Locate(mapped, out int i0, out int i1, out float u, out float dt);
        float value = SampleComponent(component, i0, i1, u, dt);
        if (cycle != 0)
            value += cycle * (_values[(_count - 1) * _dimension + component] - _values[component]);
        return value;
    }

    /// <summary>Evaluates every component into <paramref name="destination"/>, which must hold at least <see cref="Dimension"/> floats.</summary>
    public void Evaluate(float time, Span<float> destination)
    {
        if (destination.Length < _dimension)
            throw new ArgumentException($"Destination must hold at least {_dimension} float(s).", nameof(destination));
        EvaluateAll(time, destination);
    }

    /// <summary>Evaluates the first two components; components beyond <see cref="Dimension"/> read as zero.</summary>
    public Float2 EvaluateFloat2(float time)
    {
        Span<float> buffer = stackalloc float[4];
        buffer.Clear();
        EvaluateAll(time, buffer);
        return new Float2(buffer[0], buffer[1]);
    }

    /// <summary>Evaluates the first three components; components beyond <see cref="Dimension"/> read as zero.</summary>
    public Float3 EvaluateFloat3(float time)
    {
        Span<float> buffer = stackalloc float[4];
        buffer.Clear();
        EvaluateAll(time, buffer);
        return new Float3(buffer[0], buffer[1], buffer[2]);
    }

    /// <summary>Evaluates all four components; components beyond <see cref="Dimension"/> read as zero.</summary>
    public Float4 EvaluateFloat4(float time)
    {
        Span<float> buffer = stackalloc float[4];
        buffer.Clear();
        EvaluateAll(time, buffer);
        return new Float4(buffer[0], buffer[1], buffer[2], buffer[3]);
    }

    /// <summary>
    /// Evaluates the curve as a rotation. Linear segments use spherical interpolation and cubic
    /// segments are renormalised, both matching glTF. Neither <see cref="CurveWrapMode.CycleOffset"/>
    /// nor <see cref="CurveWrapMode.Extrapolate"/> means anything for a rotation, so the first behaves
    /// as <see cref="CurveWrapMode.Loop"/> and the second as <see cref="CurveWrapMode.Clamp"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the curve does not have four components.</exception>
    public Quaternion EvaluateQuaternion(float time)
    {
        if (_dimension != 4)
            throw new InvalidOperationException("EvaluateQuaternion requires a four component curve.");
        if (_count == 0) return Quaternion.Identity;
        if (_count == 1) return QuaternionAt(0);

        ResolveTime(time, out float mapped, out _);
        Locate(mapped, out int i0, out int i1, out float u, out float dt);
        if (i0 == i1 || _modes[i0] == CurveInterpolation.Step)
            return QuaternionAt(i0);
        if (_modes[i0] == CurveInterpolation.Linear)
            return Quaternion.Slerp(QuaternionAt(i0), QuaternionAt(i1), u);

        Span<float> buffer = stackalloc float[4];
        for (int c = 0; c < 4; c++)
            buffer[c] = SampleComponent(c, i0, i1, u, dt);
        var result = new Quaternion(buffer[0], buffer[1], buffer[2], buffer[3]);
        return Quaternion.NormalizeSafe(result, Quaternion.Identity);
    }

    /// <summary>
    /// Flips whole keys so no two adjacent rotations are more than a half turn apart, treating the
    /// curve as a sequence of quaternions.
    /// </summary>
    /// <remarks>
    /// A quaternion and its negation are the same rotation, and exporters are free to emit either,
    /// so a sign flip between adjacent keys is common and means nothing. It matters anyway:
    /// <see cref="CurveInterpolation.CubicSpline"/> interpolates the four components independently,
    /// and across a flip that sweeps the long way round instead of the short one. Slerp handles the
    /// flip itself, so a purely linear curve does not need this, but calling it is harmless.
    /// <para>
    /// Tangents are flipped with their key, since a tangent is a rate of change of the value it
    /// belongs to.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the curve does not have four components.</exception>
    public void EnsureQuaternionContinuity()
    {
        if (_dimension != 4)
            throw new InvalidOperationException("EnsureQuaternionContinuity requires a four component curve.");

        for (int i = 1; i < _count; i++)
        {
            int prev = (i - 1) * 4;
            int cur = i * 4;

            float dot = _values[prev] * _values[cur]
                      + _values[prev + 1] * _values[cur + 1]
                      + _values[prev + 2] * _values[cur + 2]
                      + _values[prev + 3] * _values[cur + 3];

            if (dot >= 0f) continue;

            for (int c = 0; c < 4; c++)
            {
                _values[cur + c] = -_values[cur + c];
                if (_inTangents != null) _inTangents[cur + c] = -_inTangents[cur + c];
                if (_outTangents != null) _outTangents[cur + c] = -_outTangents[cur + c];
            }
        }

        _version++;
    }

    private Quaternion QuaternionAt(int index)
    {
        int offset = index * 4;
        return new Quaternion(_values[offset], _values[offset + 1], _values[offset + 2], _values[offset + 3]);
    }

    private void EvaluateAll(float time, Span<float> destination)
    {
        if (_count == 0)
        {
            destination.Slice(0, _dimension).Clear();
            return;
        }
        if (_count == 1)
        {
            for (int c = 0; c < _dimension; c++)
                destination[c] = _values[c];
            return;
        }

        bool extrapolating =
            (time < _times[0] && PreWrap == CurveWrapMode.Extrapolate) ||
            (time > _times[_count - 1] && PostWrap == CurveWrapMode.Extrapolate);
        if (extrapolating)
        {
            for (int c = 0; c < _dimension; c++)
            {
                TryExtrapolate(c, time, out float value);
                destination[c] = value;
            }
            return;
        }

        ResolveTime(time, out float mapped, out int cycle);
        Locate(mapped, out int i0, out int i1, out float u, out float dt);
        int last = (_count - 1) * _dimension;
        for (int c = 0; c < _dimension; c++)
        {
            float value = SampleComponent(c, i0, i1, u, dt);
            if (cycle != 0)
                value += cycle * (_values[last + c] - _values[c]);
            destination[c] = value;
        }
    }

    private float SampleComponent(int component, int i0, int i1, float u, float dt)
    {
        int a = i0 * _dimension + component;
        if (i0 == i1) return _values[a];

        CurveInterpolation mode = _modes[i0];
        if (mode == CurveInterpolation.Step) return _values[a];

        int b = i1 * _dimension + component;
        float v0 = _values[a];
        float v1 = _values[b];
        if (mode == CurveInterpolation.Linear) return v0 + (v1 - v0) * u;

        float outTangent = _outTangents != null ? _outTangents[a] : 0f;
        float inTangent = _inTangents != null ? _inTangents[b] : 0f;
        return Hermite(v0, outTangent, inTangent, v1, u, dt);
    }

    /// <summary>Cubic Hermite basis with tangents expressed per second, as specified by glTF 2.0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hermite(float v0, float outTangent, float inTangent, float v1, float u, float dt)
    {
        float u2 = u * u;
        float u3 = u2 * u;
        return (2f * u3 - 3f * u2 + 1f) * v0
             + (u3 - 2f * u2 + u) * dt * outTangent
             + (-2f * u3 + 3f * u2) * v1
             + (u3 - u2) * dt * inTangent;
    }

    private bool TryExtrapolate(int component, float time, out float result)
    {
        result = 0f;
        int last = _count - 1;

        if (time < _times[0])
        {
            if (PreWrap != CurveWrapMode.Extrapolate) return false;
            result = _values[component] + EndpointSlope(component, true) * (time - _times[0]);
            return true;
        }
        if (time > _times[last])
        {
            if (PostWrap != CurveWrapMode.Extrapolate) return false;
            result = _values[last * _dimension + component] + EndpointSlope(component, false) * (time - _times[last]);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Slope the curve leaves an end key with. Cubic ends follow their handle, linear ends follow
    /// the adjacent segment, stepped ends stay flat.
    /// </summary>
    private float EndpointSlope(int component, bool atStart)
    {
        int key = atStart ? 0 : _count - 1;
        int segment = atStart ? 0 : _count - 2;
        float dt = _times[segment + 1] - _times[segment];

        switch (_modes[segment])
        {
            case CurveInterpolation.Step:
                return 0f;
            case CurveInterpolation.Linear:
                if (dt <= 0f) return 0f;
                return (_values[(segment + 1) * _dimension + component] - _values[segment * _dimension + component]) / dt;
            default:
                int offset = key * _dimension + component;
                if (atStart) return _inTangents != null ? _inTangents[offset] : 0f;
                return _outTangents != null ? _outTangents[offset] : 0f;
        }
    }

    private void ResolveTime(float time, out float mapped, out int cycle)
    {
        float start = _times[0];
        float end = _times[_count - 1];
        cycle = 0;

        if (time >= start && time <= end)
        {
            mapped = time;
            return;
        }

        float span = end - start;
        CurveWrapMode wrap = time < start ? PreWrap : PostWrap;
        if (span <= 0f || wrap == CurveWrapMode.Clamp || wrap == CurveWrapMode.Extrapolate)
        {
            mapped = time < start ? start : end;
            return;
        }

        int completed = (int)MathF.Floor((time - start) / span);
        float offset = time - start - completed * span;
        if (offset < 0f) offset = 0f;
        else if (offset > span) offset = span;

        if (wrap == CurveWrapMode.PingPong)
        {
            mapped = (completed & 1) == 0 ? start + offset : end - offset;
            return;
        }

        mapped = start + offset;
        if (wrap == CurveWrapMode.CycleOffset)
            cycle = completed;
    }

    private void Locate(float time, out int i0, out int i1, out float u, out float dt)
    {
        int last = _count - 1;
        if (time <= _times[0]) { i0 = 0; i1 = 0; u = 0f; dt = 0f; return; }
        if (time >= _times[last]) { i0 = last; i1 = last; u = 0f; dt = 0f; return; }

        int lo = 0, hi = last;
        while (lo + 1 < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_times[mid] <= time) lo = mid;
            else hi = mid;
        }

        i0 = lo;
        i1 = lo + 1;
        dt = _times[i1] - _times[i0];
        u = dt > 0f ? (time - _times[i0]) / dt : 0f;
    }

    #endregion

    #region Storage

    private Keyframe ReadKey(int index)
    {
        int offset = index * _dimension;
        Float4 value = default, inTangent = default, outTangent = default;
        for (int c = 0; c < _dimension; c++)
        {
            value[c] = _values[offset + c];
            if (_inTangents != null) inTangent[c] = _inTangents[offset + c];
            if (_outTangents != null) outTangent[c] = _outTangents[offset + c];
        }
        return new Keyframe(_times[index], value, inTangent, outTangent, _modes[index]);
    }

    private void WriteKey(int index, in Keyframe key)
    {
        Float4 value = key.Value4;
        Float4 inTangent = key.InTangent4;
        Float4 outTangent = key.OutTangent4;

        bool hasTangents = false;
        for (int c = 0; c < _dimension && !hasTangents; c++)
            hasTangents = inTangent[c] != 0f || outTangent[c] != 0f;
        if (hasTangents) EnsureTangents();

        int offset = index * _dimension;
        _times[index] = key.Time;
        _modes[index] = key.Interpolation;
        for (int c = 0; c < _dimension; c++)
        {
            _values[offset + c] = value[c];
            if (_inTangents != null) _inTangents[offset + c] = inTangent[c];
            if (_outTangents != null) _outTangents[offset + c] = outTangent[c];
        }
        _version++;
    }

    private void EnsureCapacity(int required)
    {
        if (_times.Length >= required) return;

        int capacity = _times.Length == 0 ? MinimumCapacity : _times.Length * 2;
        if (capacity < required) capacity = required;

        Array.Resize(ref _times, capacity);
        Array.Resize(ref _values, capacity * _dimension);
        Array.Resize(ref _modes, capacity);
        if (_inTangents != null) Array.Resize(ref _inTangents, capacity * _dimension);
        if (_outTangents != null) Array.Resize(ref _outTangents, capacity * _dimension);
    }

    /// <summary>Index of the first key positioned strictly after <paramref name="time"/>.</summary>
    private int UpperBound(float time)
    {
        int lo = 0, hi = _count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_times[mid] <= time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private void ThrowIfOutOfRange(int index)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Curve has {_count} key(s).");
    }

    private static void ValidateSorted(float[] times, int count)
    {
        for (int i = 1; i < count; i++)
        {
            if (times[i] < times[i - 1])
                throw new ArgumentException($"Key times must be non-decreasing; time {i} ({times[i]}) precedes time {i - 1} ({times[i - 1]}).", nameof(times));
        }
    }

    #endregion

    /// <summary>Returns an allocation free enumerator over the keys.</summary>
    public Enumerator GetEnumerator() => new Enumerator(this);

    IEnumerator<Keyframe> IEnumerable<Keyframe>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Walks the keys of an <see cref="AnimationCurve"/> in time order.</summary>
    public struct Enumerator : IEnumerator<Keyframe>
    {
        private readonly AnimationCurve _curve;
        private readonly int _version;
        private int _index;

        internal Enumerator(AnimationCurve curve)
        {
            _curve = curve;
            _version = curve._version;
            _index = -1;
            Current = default;
        }

        /// <inheritdoc/>
        public Keyframe Current { get; private set; }

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">Thrown when the curve was modified since enumeration began.</exception>
        public bool MoveNext()
        {
            ThrowIfStale();
            if (_index + 1 >= _curve._count) return false;
            _index++;
            Current = _curve.ReadKey(_index);
            return true;
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">Thrown when the curve was modified since enumeration began.</exception>
        public void Reset()
        {
            ThrowIfStale();
            _index = -1;
            Current = default;
        }

        private readonly void ThrowIfStale()
        {
            if (_version != _curve._version)
                throw new InvalidOperationException("The curve was modified while it was being enumerated.");
        }

        /// <inheritdoc/>
        public readonly void Dispose() { }
    }
}
