// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Attaches a point light to some of the particles. Lights join the scene's dynamic light BVH like any
/// other light, and never cast shadows. Keep <see cref="MaxLights"/> tight, every light costs shading time
/// on every surface it reaches.
/// </summary>
[Serializable]
public class LightModule : ParticleSystemModule
{
    [Range(0f, 1f), Tooltip("Share of particles that get a light.")]
    public float Ratio = 1f;

    [Tooltip("Hard cap on lights from this system.")]
    public int MaxLights = 20;

    [Tooltip("Multiply the light color by the particle's color.")]
    public bool UseParticleColor = true;

    public Color Color = Color.White;

    [Tooltip("Sampled over each particle's life.")]
    public MinMaxCurve Intensity = new(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

    [Tooltip("World units, sampled over each particle's life.")]
    public MinMaxCurve Range = new(2f);

    [Tooltip("Scale the range by the particle's size.")]
    public bool SizeAffectsRange = false;

    [Tooltip("Scale the intensity by the particle's alpha.")]
    public bool AlphaAffectsIntensity = true;

    private static int s_nextLightId;

    private ParticleLightProxy[] _proxies = Array.Empty<ParticleLightProxy>();
    private ForwardLightData[] _data = Array.Empty<ForwardLightData>();
    private int _active;
    private int _layer;
    private long _builtFrame = -1;

    /// <summary>Adds this frame's particle lights. The selection is built once per frame and shared by every camera.</summary>
    internal void Collect(ParticleSystemComponent system, List<IRenderableLight> lights)
    {
        if (_builtFrame != Time.FrameCount)
        {
            Build(system);
            _builtFrame = Time.FrameCount;
        }

        for (int i = 0; i < _active; i++)
            lights.Add(_proxies[i]);
    }

    private void Build(ParticleSystemComponent system)
    {
        _active = 0;
        _layer = system.GameObject.LayerIndex;

        int max = Math.Clamp(MaxLights, 0, 4096);
        ReadOnlySpan<Particle> particles = system.Particles;
        float sizeScale = system.SizeScale;

        for (int i = 0; i < particles.Length && _active < max; i++)
        {
            ref readonly Particle p = ref particles[i];
            if (Ratio < 1f && p.Random(0x141) >= Ratio) continue;

            EnsureCapacity(_active + 1);

            float age = p.NormalizedAge;
            Color rgb = UseParticleColor ? p.Color * Color : Color;
            float intensity = Intensity.Evaluate(age, p.Random(0x142));
            if (AlphaAffectsIntensity) intensity *= Maths.Saturate(p.Color.A);
            float range = MathF.Max(0.01f, Range.Evaluate(age, p.Random(0x143)));
            if (SizeAffectsRange) range *= (MathF.Abs(p.Size.X) + MathF.Abs(p.Size.Y)) * 0.5f * sizeScale;

            _data[_active] = new ForwardLightData
            {
                Type = LightType.Point,
                Position = system.SimPointToWorld(p.Position),
                Direction = Float3.UnitY,
                Color = new Float3(rgb.R, rgb.G, rgb.B),
                Intensity = MathF.Max(0f, intensity),
                Range = range,
            };
            _active++;
        }
    }

    private void EnsureCapacity(int count)
    {
        if (_proxies.Length >= count) return;

        int size = Math.Max(count, _proxies.Length * 2);
        int old = _proxies.Length;
        Array.Resize(ref _proxies, size);
        Array.Resize(ref _data, size);
        for (int i = old; i < size; i++)
            _proxies[i] = new ParticleLightProxy(this, i, Interlocked.Decrement(ref s_nextLightId));
    }

    /// <summary>
    /// One stable light per slot. The light BVH keys lights by reference, so reusing the same proxy lets
    /// it refit a leaf in place when the light moves instead of rebuilding its topology.
    /// Ids count down from -1 so they never meet a component's instance id.
    /// </summary>
    private sealed class ParticleLightProxy(LightModule owner, int slot, int id) : IRenderableLight
    {
        public int GetLightID() => id;
        public int GetLayer() => owner._layer;
        public LightType GetLightType() => LightType.Point;
        public Float3 GetLightPosition() => owner._data[slot].Position;
        public Float3 GetLightDirection() => Float3.UnitY;
        public bool DoCastShadows() => false;
        public ForwardLightData GetForwardLightData() => owner._data[slot];
    }
}
