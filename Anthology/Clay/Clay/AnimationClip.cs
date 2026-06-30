namespace Prowl.Clay;

/// <summary>Property animated by an <see cref="AnimationBinding"/>.</summary>
public enum AnimatedProperty
{
    /// <summary><see cref="ModelNode.LocalPosition"/>.</summary>
    Position,
    /// <summary><see cref="ModelNode.LocalRotation"/>.</summary>
    Rotation,
    /// <summary><see cref="ModelNode.LocalScale"/>.</summary>
    Scale,
    /// <summary>Per-blend-shape weight (<see cref="AnimationBinding.SubIndex"/> selects the shape).</summary>
    BlendShapeWeight,
    /// <summary>Node visibility / enabled state, when the source carries it.</summary>
    Visibility,
}

/// <summary>Interpolation between keyframes on an <see cref="AnimationCurve"/>.</summary>
public enum AnimationInterpolation
{
    /// <summary>Step: the value at time t is the value of the latest key whose time is &lt;= t.</summary>
    Step,
    /// <summary>Linear: linear interpolation between adjacent keys (slerp for quaternions).</summary>
    Linear,
    /// <summary>Cubic Hermite spline; <see cref="AnimationCurve.Values"/> is laid out as
    /// (in-tangent, value, out-tangent) per key.</summary>
    CubicSpline,
}

/// <summary>
/// A named animation clip: a duration and a list of curve bindings.
/// </summary>
public sealed class AnimationClip
{
    /// <summary>Clip name.</summary>
    public required string Name { get; init; }

    /// <summary>Total duration in seconds, equal to the largest end-time across all bindings.</summary>
    public float Duration { get; init; }

    /// <summary>Curves driving node transforms or blend-shape weights.</summary>
    public required AnimationBinding[] Bindings { get; init; }
}
