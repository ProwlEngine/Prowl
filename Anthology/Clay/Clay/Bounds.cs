using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// Axis-aligned bounding box in local space, expressed as a min/max pair of single-precision points.
/// </summary>
/// <remarks>
/// Defined locally inside Prowl.Clay rather than reusing <c>Prowl.Vector.AABB</c> to keep the public
/// surface independent of the math library's geometry types.
/// </remarks>
public struct Bounds : IEquatable<Bounds>
{
    /// <summary>Minimum corner.</summary>
    public Float3 Min;

    /// <summary>Maximum corner.</summary>
    public Float3 Max;

    /// <summary>
    /// Returns a <see cref="Bounds"/> that represents the empty set, suitable for accumulation via
    /// <see cref="Encapsulate(Float3)"/>.
    /// </summary>
    public static Bounds Empty => new()
    {
        Min = new Float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
        Max = new Float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity),
    };

    /// <summary>Initializes a new bounds from explicit min and max corners.</summary>
    public Bounds(Float3 min, Float3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Center of the bounds.</summary>
    public readonly Float3 Center => (Min + Max) * 0.5f;

    /// <summary>Half-size (extents) of the bounds.</summary>
    public readonly Float3 Extents => (Max - Min) * 0.5f;

    /// <summary>Full size of the bounds.</summary>
    public readonly Float3 Size => Max - Min;

    /// <summary>True when min is greater than max on any axis (no points have been encapsulated).</summary>
    public readonly bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    /// <summary>Expands the bounds to include the supplied point.</summary>
    public void Encapsulate(Float3 point)
    {
        if (point.X < Min.X) Min.X = point.X;
        if (point.Y < Min.Y) Min.Y = point.Y;
        if (point.Z < Min.Z) Min.Z = point.Z;
        if (point.X > Max.X) Max.X = point.X;
        if (point.Y > Max.Y) Max.Y = point.Y;
        if (point.Z > Max.Z) Max.Z = point.Z;
    }

    /// <summary>Expands the bounds to include another bounds.</summary>
    public void Encapsulate(Bounds other)
    {
        if (other.IsEmpty)
            return;
        Encapsulate(other.Min);
        Encapsulate(other.Max);
    }

    /// <inheritdoc />
    public readonly bool Equals(Bounds other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is Bounds b && Equals(b);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Min, Max);

    /// <inheritdoc />
    public override readonly string ToString() => $"Bounds(Min={Min}, Max={Max})";
}
