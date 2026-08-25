// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Vector;

/// <summary>Describes how an <see cref="AnimationCurve"/> behaves across the segment leaving a key.</summary>
public enum CurveInterpolation : byte
{
    /// <summary>Straight line from this key to the next.</summary>
    Linear,
    /// <summary>Holds this key's value until the next key is reached.</summary>
    Step,
    /// <summary>Cubic Hermite spline using this key's out tangent and the next key's in tangent.</summary>
    CubicSpline
}

/// <summary>Describes how an <see cref="AnimationCurve"/> behaves outside the time range its keys cover.</summary>
public enum CurveWrapMode : byte
{
    /// <summary>Holds the value of the nearest end key.</summary>
    Clamp,
    /// <summary>Repeats the curve from the start.</summary>
    Loop,
    /// <summary>Repeats the curve, reversing direction on every other cycle.</summary>
    PingPong,
    /// <summary>Repeats the curve, offsetting each cycle by the end-to-start value delta so the result stays continuous.</summary>
    CycleOffset,
    /// <summary>Continues along the slope at the nearest end key.</summary>
    Extrapolate
}

/// <summary>Rule used by <see cref="AnimationCurve.SmoothTangents(CurveTangentMode)"/> to derive a tangent.</summary>
public enum CurveTangentMode : byte
{
    /// <summary>Zero slope, producing a horizontal handle.</summary>
    Flat,
    /// <summary>Slope of the straight line to the adjacent key on that side.</summary>
    Linear,
    /// <summary>Catmull-Rom slope through the surrounding keys.</summary>
    Auto,
    /// <summary>Like <see cref="Auto"/>, but flattened at local minima and maxima so the curve never overshoots them.</summary>
    ClampedAuto
}

/// <summary>
/// A single control point on an <see cref="AnimationCurve"/>. Holds up to four components; a curve
/// only reads the first <see cref="AnimationCurve.Dimension"/> of them.
/// </summary>
/// <remarks>
/// Tangents are slopes in value units per second, matching glTF <c>CUBICSPLINE</c> samplers, so a
/// tangent of 2 always means rising by 2 per second regardless of how far apart the keys sit.
/// </remarks>
[Serializable]
public struct Keyframe : IEquatable<Keyframe>, IComparable<Keyframe>
{
    /// <summary>Position of the key along the curve, in seconds.</summary>
    public float Time;

    /// <summary>All four value components. The narrower accessors are views onto this.</summary>
    public Float4 Value4;

    /// <summary>All four in-tangent components. The narrower accessors are views onto this.</summary>
    public Float4 InTangent4;

    /// <summary>All four out-tangent components. The narrower accessors are views onto this.</summary>
    public Float4 OutTangent4;

    /// <summary>Interpolation applied between this key and the one after it.</summary>
    public CurveInterpolation Interpolation;

    /// <summary>First value component.</summary>
    public float Value { readonly get => Value4.X; set => Value4.X = value; }

    /// <summary>First two value components.</summary>
    public Float2 Value2 { readonly get => new Float2(Value4.X, Value4.Y); set { Value4.X = value.X; Value4.Y = value.Y; } }

    /// <summary>First three value components.</summary>
    public Float3 Value3 { readonly get => new Float3(Value4.X, Value4.Y, Value4.Z); set { Value4.X = value.X; Value4.Y = value.Y; Value4.Z = value.Z; } }

    /// <summary>First in-tangent component; the slope approaching this key from the previous one.</summary>
    public float InTangent { readonly get => InTangent4.X; set => InTangent4.X = value; }

    /// <summary>First two in-tangent components.</summary>
    public Float2 InTangent2 { readonly get => new Float2(InTangent4.X, InTangent4.Y); set { InTangent4.X = value.X; InTangent4.Y = value.Y; } }

    /// <summary>First three in-tangent components.</summary>
    public Float3 InTangent3 { readonly get => new Float3(InTangent4.X, InTangent4.Y, InTangent4.Z); set { InTangent4.X = value.X; InTangent4.Y = value.Y; InTangent4.Z = value.Z; } }

    /// <summary>First out-tangent component; the slope leaving this key toward the next one.</summary>
    public float OutTangent { readonly get => OutTangent4.X; set => OutTangent4.X = value; }

    /// <summary>First two out-tangent components.</summary>
    public Float2 OutTangent2 { readonly get => new Float2(OutTangent4.X, OutTangent4.Y); set { OutTangent4.X = value.X; OutTangent4.Y = value.Y; } }

    /// <summary>First three out-tangent components.</summary>
    public Float3 OutTangent3 { readonly get => new Float3(OutTangent4.X, OutTangent4.Y, OutTangent4.Z); set { OutTangent4.X = value.X; OutTangent4.Y = value.Y; OutTangent4.Z = value.Z; } }

    /// <summary>Creates a linear key.</summary>
    public Keyframe(float time, float value)
        : this(time, new Float4(value, 0f, 0f, 0f), Float4.Zero, Float4.Zero, CurveInterpolation.Linear) { }

    /// <summary>Creates a cubic key with explicit tangents.</summary>
    public Keyframe(float time, float value, float inTangent, float outTangent)
        : this(time, new Float4(value, 0f, 0f, 0f), new Float4(inTangent, 0f, 0f, 0f), new Float4(outTangent, 0f, 0f, 0f), CurveInterpolation.CubicSpline) { }

    /// <summary>Creates a key with an explicit interpolation mode.</summary>
    public Keyframe(float time, float value, float inTangent, float outTangent, CurveInterpolation interpolation)
        : this(time, new Float4(value, 0f, 0f, 0f), new Float4(inTangent, 0f, 0f, 0f), new Float4(outTangent, 0f, 0f, 0f), interpolation) { }

    /// <summary>Creates a linear two-component key.</summary>
    public Keyframe(float time, Float2 value)
        : this(time, new Float4(value.X, value.Y, 0f, 0f), Float4.Zero, Float4.Zero, CurveInterpolation.Linear) { }

    /// <summary>Creates a cubic two-component key with explicit tangents.</summary>
    public Keyframe(float time, Float2 value, Float2 inTangent, Float2 outTangent)
        : this(time, new Float4(value.X, value.Y, 0f, 0f), new Float4(inTangent.X, inTangent.Y, 0f, 0f), new Float4(outTangent.X, outTangent.Y, 0f, 0f), CurveInterpolation.CubicSpline) { }

    /// <summary>Creates a linear three-component key.</summary>
    public Keyframe(float time, Float3 value)
        : this(time, new Float4(value.X, value.Y, value.Z, 0f), Float4.Zero, Float4.Zero, CurveInterpolation.Linear) { }

    /// <summary>Creates a cubic three-component key with explicit tangents.</summary>
    public Keyframe(float time, Float3 value, Float3 inTangent, Float3 outTangent)
        : this(time, new Float4(value.X, value.Y, value.Z, 0f), new Float4(inTangent.X, inTangent.Y, inTangent.Z, 0f), new Float4(outTangent.X, outTangent.Y, outTangent.Z, 0f), CurveInterpolation.CubicSpline) { }

    /// <summary>Creates a linear four-component key.</summary>
    public Keyframe(float time, Float4 value)
        : this(time, value, Float4.Zero, Float4.Zero, CurveInterpolation.Linear) { }

    /// <summary>Creates a cubic four-component key with explicit tangents.</summary>
    public Keyframe(float time, Float4 value, Float4 inTangent, Float4 outTangent)
        : this(time, value, inTangent, outTangent, CurveInterpolation.CubicSpline) { }

    /// <summary>Creates a four-component key with an explicit interpolation mode.</summary>
    public Keyframe(float time, Float4 value, Float4 inTangent, Float4 outTangent, CurveInterpolation interpolation)
    {
        Time = time;
        Value4 = value;
        InTangent4 = inTangent;
        OutTangent4 = outTangent;
        Interpolation = interpolation;
    }

    /// <summary>Returns a copy of this key using a different interpolation mode.</summary>
    public readonly Keyframe WithInterpolation(CurveInterpolation interpolation) => new Keyframe(Time, Value4, InTangent4, OutTangent4, interpolation);

    /// <summary>Returns a copy of this key positioned at a different time.</summary>
    public readonly Keyframe WithTime(float time) => new Keyframe(time, Value4, InTangent4, OutTangent4, Interpolation);

    /// <summary>Orders keys by <see cref="Time"/>.</summary>
    public readonly int CompareTo(Keyframe other) => Time.CompareTo(other.Time);

    /// <inheritdoc/>
    public readonly bool Equals(Keyframe other) =>
        Time.Equals(other.Time) &&
        Value4.Equals(other.Value4) &&
        InTangent4.Equals(other.InTangent4) &&
        OutTangent4.Equals(other.OutTangent4) &&
        Interpolation == other.Interpolation;

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is Keyframe other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode()
    {
        int hash = Time.GetHashCode();
        hash = (hash * 397) ^ Value4.GetHashCode();
        hash = (hash * 397) ^ InTangent4.GetHashCode();
        hash = (hash * 397) ^ OutTangent4.GetHashCode();
        return (hash * 397) ^ (int)Interpolation;
    }

    /// <summary>Compares two keys for equality.</summary>
    public static bool operator ==(Keyframe left, Keyframe right) => left.Equals(right);

    /// <summary>Compares two keys for inequality.</summary>
    public static bool operator !=(Keyframe left, Keyframe right) => !left.Equals(right);

    /// <inheritdoc/>
    public readonly override string ToString() => Time.ToString() + ": " + Value4.ToString() + " (" + Interpolation.ToString() + ")";
}
