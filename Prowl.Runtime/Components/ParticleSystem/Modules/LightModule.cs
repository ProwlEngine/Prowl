// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

using GLightType = Prowl.Graphite.Rendering.LightType;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Attaches a point light to some of the particles. They are world lights like any other, and never cast shadows.
/// Keep <see cref="MaxLights"/> tight, every light costs shading time on every surface it reaches.
/// </summary>
[Serializable]
public class LightModule : ParticleSystemModule, ISerializationCallbackReceiver
{
    [Range(0f, 1f), Tooltip("Share of particles that get a light.")]
    public float Ratio = 1f;

    [Tooltip("Hard cap on lights from this system.")]
    public int MaxLights = 20;

    [Tooltip("Multiply the light color by the particle's color.")]
    public bool UseParticleColor = true;

    public Color Color = Color.White;

    [Tooltip("Candela, sampled over each particle's life.")]
    public MinMaxCurve Intensity = new(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)), 1000f);

    // 1 once saved with physical units, 0 for modules saved before
    [SerializeField, HideInInspector] private int units;

    [Tooltip("World units, sampled over each particle's life.")]
    public MinMaxCurve Range = new(2f);

    [Tooltip("Scale the range by the particle's size.")]
    public bool SizeAffectsRange = false;

    [Tooltip("Scale the intensity by the particle's alpha.")]
    public bool AlphaAffectsIntensity = true;

    void ISerializationCallbackReceiver.OnBeforeSerialize() => units = 1;

    // A module saved before its units were physical keeps its look, see LegacyLightUnits
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (units != 0) return;
        Intensity.ConstantValue *= Rendering.LegacyLightUnits.Light;
        Intensity.MinValue *= Rendering.LegacyLightUnits.Light;
        Intensity.MaxValue *= Rendering.LegacyLightUnits.Light;
        Intensity.CurveMultiplier *= Rendering.LegacyLightUnits.Light;
        units = 1;
    }

    // One light per slot, kept while the slot is in use so a moving light is updated rather than added again
    [NonSerialized] private LightHandle[] _lights = [];
    [NonSerialized] private RenderWorld? _world;

    /// <summary>Brings this frame's particle lights into the world. Main thread, before the frame is submitted.</summary>
    internal void Sync(ParticleSystemComponent system, RenderWorld world)
    {
        if (_world != world)
            Detach();
        _world = world;

        int active = 0;
        if (Enabled && system.ParticleCount > 0)
        {
            int max = Math.Clamp(MaxLights, 0, 4096);
            ReadOnlySpan<Particle> particles = system.Particles;
            float sizeScale = system.SizeScale;

            for (int i = 0; i < particles.Length && active < max; i++)
            {
                ref readonly Particle p = ref particles[i];
                if (Ratio < 1f && p.Random(0x141) >= Ratio) continue;

                float age = p.NormalizedAge;
                Color rgb = UseParticleColor ? p.Color * Color : Color;
                float intensity = Intensity.Evaluate(age, p.Random(0x142));
                if (AlphaAffectsIntensity) intensity *= Maths.Saturate(p.Color.A);
                float range = MathF.Max(0.01f, Range.Evaluate(age, p.Random(0x143)));
                if (SizeAffectsRange) range *= (MathF.Abs(p.Size.X) + MathF.Abs(p.Size.Y)) * 0.5f * sizeScale;

                LightDesc desc = new()
                {
                    Type = GLightType.Point,
                    Position = system.SimPointToWorld(p.Position),
                    Color = new Float3(rgb.R, rgb.G, rgb.B),
                    Intensity = MathF.Max(0f, intensity),
                    Range = range,
                };

                if (_lights.Length <= active)
                    Array.Resize(ref _lights, Math.Max(active + 1, _lights.Length * 2));
                if (_lights[active].IsValid && world.IsAlive(_lights[active]))
                    world.SetLight(_lights[active], desc);
                else
                    _lights[active] = world.AddLight(desc);
                active++;
            }
        }

        for (int i = active; i < _lights.Length; i++)
            Remove(world, i);
    }

    /// <summary>Removes every particle light from the world it was added to.</summary>
    internal void Detach()
    {
        if (_world != null)
            for (int i = 0; i < _lights.Length; i++)
                Remove(_world, i);
        _world = null;
    }

    private void Remove(RenderWorld world, int slot)
    {
        if (_lights[slot].IsValid && world.IsAlive(_lights[slot]))
            world.RemoveLight(_lights[slot]);
        _lights[slot] = default;
    }
}
