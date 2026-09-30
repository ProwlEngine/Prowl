// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem;

/// <summary>
/// Represents a single particle in the particle system.
/// This struct is used for GPU instancing.
/// </summary>
public struct Particle
{
    public Float3 Position;
    public Float3 Velocity;
    public float Rotation;
    public float RotationalSpeed;
    public float StartSize;
    public float Size;
    public Color StartColor;
    public Color Color;
    public float StartLifetime;
    public float Lifetime;
    public uint RandomSeed;

    // UV animation data
    public float UVFrame;        // Current animation frame
    public float TotalTime;      // Total time particle has been alive

    /// <summary>
    /// Gets the normalized lifetime (0 to 1) of the particle.
    /// </summary>
    public float NormalizedLifetime => 1.0f - (Lifetime / StartLifetime);

    /// <summary>
    /// Returns true if the particle is still alive.
    /// </summary>
    public bool IsAlive => Lifetime > 0;
}
