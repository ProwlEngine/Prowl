// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// The values a particle is born with. Curves here are sampled at the system's normalized time
/// (how far through <see cref="ParticleSystemComponent.Duration"/> it is) when the particle spawns.
/// Always active, the toggle is ignored.
/// </summary>
[Serializable]
public class InitialModule : ParticleSystemModule
{
    [Tooltip("Seconds each particle lives.")]
    public MinMaxCurve StartLifetime = new(5.0f);

    [Tooltip("Speed along the direction the shape emits in.")]
    public MinMaxCurve StartSpeed = new(5.0f);

    [Tooltip("Size each axis on its own.")]
    public bool StartSize3D = false;
    [Tooltip("Uniform size, or the X size when 3D.")]
    public MinMaxCurve StartSize = new(1.0f);
    [ShowIf(nameof(StartSize3D))]
    public MinMaxCurve StartSizeY = new(1.0f);
    [ShowIf(nameof(StartSize3D))]
    public MinMaxCurve StartSizeZ = new(1.0f);

    [Tooltip("Rotate each axis on its own. Billboards only use Z.")]
    public bool StartRotation3D = false;
    [ShowIf(nameof(StartRotation3D))]
    public MinMaxCurve StartRotationX = new(0.0f);
    [ShowIf(nameof(StartRotation3D))]
    public MinMaxCurve StartRotationY = new(0.0f);
    [Tooltip("Rotation in degrees, around Z for billboards.")]
    public MinMaxCurve StartRotation = new(0.0f);

    [Range(0f, 1f), Tooltip("Chance a particle spins the other way.")]
    public float FlipRotation = 0f;

    public MinMaxGradient StartColor = new(Color.White);

    [Tooltip("Multiplier on world gravity (0, -9.81, 0).")]
    public float GravityModifier = 0.0f;

    public InitialModule() => Enabled = true;

    internal const uint SaltFlip = 0x51A7u;

    internal void Apply(ref Particle p, float systemTime01, System.Random random)
    {
        p.StartLifetime = MathF.Max(0f, StartLifetime.Evaluate(systemTime01, random));
        p.Lifetime = p.StartLifetime;

        float size = StartSize.Evaluate(systemTime01, random);
        p.StartSize = StartSize3D
            ? new Float3(size, StartSizeY.Evaluate(systemTime01, random), StartSizeZ.Evaluate(systemTime01, random))
            : new Float3(size);
        p.Size = p.StartSize;

        float z = StartRotation.Evaluate(systemTime01, random);
        p.Rotation = StartRotation3D
            ? new Float3(StartRotationX.Evaluate(systemTime01, random), StartRotationY.Evaluate(systemTime01, random), z)
            : new Float3(0f, 0f, z);
        if (IsFlipped(in p))
            p.Rotation = -p.Rotation;

        p.StartColor = StartColor.Evaluate(systemTime01, random);
        p.Color = p.StartColor;
    }

    internal float Speed(float systemTime01, System.Random random) => StartSpeed.Evaluate(systemTime01, random);

    /// <summary>+1 or -1, the direction this particle spins in.</summary>
    internal float RotationSign(in Particle p) => IsFlipped(in p) ? -1f : 1f;

    private bool IsFlipped(in Particle p) => FlipRotation > 0f && p.Random(SaltFlip) < FlipRotation;
}
