// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Spherical zone of wind shaped like a downwash: air comes down through the middle, spreads out
/// across the ground and dies at the rim. The middle is calm, not the strongest point, which is what
/// makes it read as something hovering rather than a starburst.
/// Terrain grass blends the nearest <see cref="kMaxShaderZones"/> zones per frame, particle systems
/// take the single nearest one.
/// </summary>
[AddComponentMenu("Effects/Wind Zone")]
[ComponentIcon("\uf863")] // Fan
[ExecuteAlways]
public sealed class WindZone : MonoBehaviour
{
    /// <summary>How many zones one shader can blend at once.</summary>
    public const int kMaxShaderZones = 4;

    /// <summary>Radius of influence in world units. Wind is zero at and beyond this distance.</summary>
    public float Radius = 10f;

    /// <summary>Wind force at the center of the zone.</summary>
    public float WindMain = 1f;

    /// <summary>Position-varying jitter on the force, as a fraction of <see cref="WindMain"/>.</summary>
    public float Turbulence = 0.5f;

    /// <summary>Gust amplitude, as a fraction of <see cref="WindMain"/>.</summary>
    public float PulseMagnitude = 0.5f;

    /// <summary>Gusts per second. They roll outward as rings rather than pulsing the whole zone at once.</summary>
    public float PulseFrequency = 0.25f;

    private static readonly List<WindZone> s_active = [];

    private static readonly string[] s_sphereUniforms =
        ["_WindZoneSphere[0]", "_WindZoneSphere[1]", "_WindZoneSphere[2]", "_WindZoneSphere[3]"];
    private static readonly string[] s_paramUniforms =
        ["_WindZoneParams[0]", "_WindZoneParams[1]", "_WindZoneParams[2]", "_WindZoneParams[3]"];

    /// <summary>Every enabled zone in the scene.</summary>
    public static IReadOnlyList<WindZone> Active => s_active;

    public override void OnEnable()
    {
        base.OnEnable();
        if (!s_active.Contains(this))
            s_active.Add(this);
    }

    public override void OnDisable()
    {
        base.OnDisable();
        s_active.Remove(this);
    }

    /// <summary>Wind velocity this zone applies at a world position, using the current time.</summary>
    public Float3 SampleWind(Float3 worldPosition) => SampleWind(worldPosition, Time.TimeSinceStartup);

    /// <summary>
    /// Wind velocity this zone applies at a world position: outflow across the ground plane plus the
    /// column coming down through the middle. The grass shader runs the same profile, so a particle
    /// and a blade in the same spot are pushed the same way.
    /// </summary>
    public Float3 SampleWind(Float3 worldPosition, float time)
    {
        Float3 toPoint = worldPosition - Transform.Position;
        float radius = MathF.Max(Radius, 1e-4f);

        float horizontal = MathF.Sqrt(toPoint.X * toPoint.X + toPoint.Z * toPoint.Z);
        float height = MathF.Abs(toPoint.Y);
        if (horizontal >= radius || height >= radius) return Float3.Zero;

        // Height falls off on its own, so a zone parked high overhead barely stirs the ground
        float vertical = 1f - Smoothstep(0f, radius, height);
        float r = horizontal / radius;

        Float2 outward = horizontal > 1e-4f
            ? new Float2(toPoint.X / horizontal, toPoint.Z / horizontal)
            : Float2.Zero;

        // Calm eye, peaking where the column spreads, then a long decay to the rim
        float outflow = Smoothstep(0f, 0.22f, r) * (1f - Smoothstep(0.35f, 1f, r));
        // The column itself, which is what the middle gets instead of outflow
        float downdraft = 1f - Smoothstep(0f, 0.5f, r);

        // Gust fronts travelling outward. Time is a phase on a wave running out from the middle,
        // never a multiplier on anything that moves with the zone, so nudging the zone shifts the
        // pattern by what it moved rather than scrambling it. The grass shader runs the same
        // structure with value noise for finer detail.
        float gustSpeed = 0.35f * (1f + WindMain);
        float ringPhase = r * 3f - time * gustSpeed;
        float front = MathF.Sin(outward.X * 2.5f + ringPhase) * MathF.Cos(outward.Y * 2.5f);
        float wobble = MathF.Sin(outward.Y * 2.5f + ringPhase * 1.3f + 17f) * MathF.Cos(outward.X * 2.5f);

        float gust = 1f + front * Turbulence
                   + MathF.Sin((time * PulseFrequency - r * 2f) * MathF.Tau) * PulseMagnitude;
        float speed = MathF.Max(WindMain * vertical * gust, 0f);

        // Turning the flow stays visible after a blade is already flat, where more push does not
        float twist = wobble * Turbulence * 0.8f;
        float cs = MathF.Cos(twist), sn = MathF.Sin(twist);
        Float2 flow = new(outward.X * cs - outward.Y * sn, outward.X * sn + outward.Y * cs);

        return new Float3(flow.X * speed * outflow, -speed * downdraft, flow.Y * speed * outflow);
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Maths.Clamp((x - edge0) / MathF.Max(edge1 - edge0, 1e-6f), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>The zone reaching <paramref name="worldPosition"/> from closest range, or null if there are none.</summary>
    public static WindZone? GetNearest(Float3 worldPosition)
    {
        WindZone? nearest = null;
        float nearestKey = float.MaxValue;

        for (int i = 0; i < s_active.Count; i++)
        {
            WindZone zone = s_active[i];
            if (zone.IsNotValid()) continue;

            float key = SortKey(zone, worldPosition);
            if (key >= nearestKey) continue;

            nearest = zone;
            nearestKey = key;
        }

        return nearest;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the zones reaching <paramref name="worldPosition"/>
    /// from closest range, nearest first, and returns how many were written.
    /// </summary>
    public static int GetNearest(Float3 worldPosition, Span<WindZone?> destination)
    {
        if (destination.Length == 0) return 0;

        Span<float> keys = stackalloc float[destination.Length];
        int count = 0;

        for (int i = 0; i < s_active.Count; i++)
        {
            WindZone zone = s_active[i];
            if (zone.IsNotValid()) continue;

            float key = SortKey(zone, worldPosition);
            if (count == destination.Length && key >= keys[count - 1]) continue;

            int slot = Math.Min(count, destination.Length - 1);
            while (slot > 0 && keys[slot - 1] > key)
            {
                keys[slot] = keys[slot - 1];
                destination[slot] = destination[slot - 1];
                slot--;
            }

            keys[slot] = key;
            destination[slot] = zone;
            if (count < destination.Length) count++;
        }

        return count;
    }

    /// <summary>Distance from the point to the zone's surface. Negative inside the zone.</summary>
    private static float SortKey(WindZone zone, Float3 worldPosition)
        => Float3.Distance(worldPosition, zone.Transform.Position) - MathF.Max(zone.Radius, 1e-4f);

    /// <summary>Upload zones as _WindZone uniforms. Slots past <paramref name="count"/> are zeroed out.</summary>
    public static void SetMaterialUniforms(Material material, ReadOnlySpan<WindZone?> zones, int count)
    {
        int used = Math.Min(count, Math.Min(zones.Length, kMaxShaderZones));
        material.SetInt("_WindZoneCount", used);

        for (int i = 0; i < kMaxShaderZones; i++)
        {
            Float4 sphere = Float4.Zero;
            Float4 parameters = Float4.Zero;

            if (i < used)
            {
                WindZone? zone = zones[i];
                if (zone.IsValid())
                {
                    Float3 center = zone.Transform.Position;
                    sphere = new Float4(center.X, center.Y, center.Z, MathF.Max(zone.Radius, 1e-4f));
                    parameters = new Float4(zone.WindMain, zone.Turbulence, zone.PulseMagnitude, zone.PulseFrequency);
                }
            }

            material.SetVector(s_sphereUniforms[i], sphere);
            material.SetVector(s_paramUniforms[i], parameters);
        }
    }

    public override void DrawGizmos()
    {
        var color = new Color(0.45f, 0.85f, 1f, 1f);
        Float3 center = Transform.Position;
        Debug.DrawWireSphere(center, Radius, color);

        // Four outward spokes so the push direction reads at a glance
        float spoke = Radius * 0.35f;
        Debug.DrawLine(center, center + new Float3(spoke, 0, 0), color);
        Debug.DrawLine(center, center + new Float3(-spoke, 0, 0), color);
        Debug.DrawLine(center, center + new Float3(0, 0, spoke), color);
        Debug.DrawLine(center, center + new Float3(0, 0, -spoke), color);
    }
}
