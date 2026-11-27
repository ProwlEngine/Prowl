// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

/// <summary>
/// A named animation clip: a time range and a list of curve bindings.
/// </summary>
public sealed class AnimationClip
{
    /// <summary>Clip name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Time of the earliest key across all bindings. Usually zero, but a clip authored on a shared
    /// timeline can start later, and playing it from zero would sit on its first pose for the gap.
    /// </summary>
    public float StartTime { get; init; }

    /// <summary>Time of the latest key across all bindings.</summary>
    public float EndTime { get; init; }

    /// <summary>Length of the clip, <see cref="EndTime"/> minus <see cref="StartTime"/>.</summary>
    public float Duration => EndTime - StartTime;

    /// <summary>Curves driving node transforms or blend-shape weights.</summary>
    public required AnimationBinding[] Bindings { get; init; }
}
