// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// Mutable counterpart of <see cref="AnimationClip"/>.
/// </summary>
internal sealed class IntermediateAnimation
{
    public string Name { get; set; } = string.Empty;
    public List<IntermediateAnimationBinding> Bindings { get; } = new();
}

/// <summary>
/// Mutable counterpart of <see cref="AnimationBinding"/>. Values stay in the source's flat layout
/// through the pipeline so the coordinate-conversion step can negate and swap components in place;
/// they become a <see cref="AnimationCurve"/> at bake.
/// </summary>
internal sealed class IntermediateAnimationBinding
{
    public IntermediateNode? TargetNode { get; set; }
    public AnimatedProperty Property { get; set; }
    public int SubIndex { get; set; }
    public CurveInterpolation Interpolation { get; set; } = CurveInterpolation.Linear;
    public int Dimension { get; set; }
    public List<float> Times { get; } = new();

    /// <summary>
    /// For step and linear, <c>Dimension</c> floats per key. For cubic spline, <c>3 * Dimension</c>
    /// laid out as in-tangent, value, out-tangent per key, which is glTF's sampler layout.
    /// </summary>
    public List<float> Values { get; } = new();

    /// <summary>Floats per key, which the cubic layout triples.</summary>
    public int ValuesPerKey => Interpolation == CurveInterpolation.CubicSpline ? Dimension * 3 : Dimension;
}
