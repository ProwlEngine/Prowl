// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// Mutable counterpart of <see cref="AnimationClip"/>.
/// </summary>
internal sealed class IntermediateAnimation
{
    public string Name { get; set; } = string.Empty;
    public List<IntermediateAnimationBinding> Bindings { get; } = new();
    public float Duration { get; set; }
}

internal sealed class IntermediateAnimationBinding
{
    public IntermediateNode? TargetNode { get; set; }
    public AnimatedProperty Property { get; set; }
    public int SubIndex { get; set; }
    public AnimationInterpolation Interpolation { get; set; } = AnimationInterpolation.Linear;
    public int Dimension { get; set; }
    public List<float> Times { get; } = new();
    public List<float> Values { get; } = new();
}
