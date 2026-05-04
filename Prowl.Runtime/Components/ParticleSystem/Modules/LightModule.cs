// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// When enabled, every alive particle emits a point light into the scene. Lights register
/// with the dynamic <c>SceneLightSystem</c> BVH and so contribute to the same forward-lit
/// surfaces all other lights do, including BRDF, fog scattering, etc. Particles never cast
/// shadows (they'd swamp the closest-N atlas budget instantly).
///
/// Cost: each frame the particle proxies refit the dynamic BVH. For 1000 particles that's
/// ~one tree rebuild per frame, low microseconds. Don't enable this on million-particle
/// debris systems unless you've measured it.
/// </summary>
[Serializable]
public class LightModule : ParticleSystemModule
{
    /// <summary>Tint multiplied with the particle's colour (or used directly when
    /// <see cref="UseParticleColor"/> is false).</summary>
    public Color Color = Color.White;

    /// <summary>If true, the emitted light's RGB comes from the particle's current colour
    /// (after any ColorOverLifetime processing); the module's <see cref="Color"/> tints it.
    /// If false, the module's colour is used directly.</summary>
    public bool UseParticleColor = true;

    /// <summary>Base intensity scalar, applied on top of the colour.</summary>
    public float Intensity = 1.0f;

    /// <summary>Light range in world units. Larger ranges give visibly bigger BVH AABBs and
    /// so cost more fragments per pixel; keep this tight to the actual visible glow.</summary>
    public float Range = 2.0f;

    /// <summary>When true, the light's effective range scales with the particle's current
    /// size (range becomes <c>Range * particle.Size / particle.StartSize</c>). Useful for
    /// expanding fireballs / shrinking sparks.</summary>
    public bool ScaleRangeByParticleSize = false;

    /// <summary>When true, the light fades to zero intensity as the particle approaches the
    /// end of its lifetime (<c>intensity *= 1 - normalizedLifetime</c>). When false, the
    /// intensity stays at <see cref="Intensity"/> for the particle's whole life.</summary>
    public bool FadeWithLifetime = true;
}
