// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Spherical zone of wind. Air pushes outward from the center and fades to nothing at the radius.
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

    /// <summary>Gusts per second.</summary>
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
    /// Wind velocity this zone applies at a world position. Mirrors the math the grass shader runs,
    /// so a particle and a grass blade in the same spot are pushed the same way.
    /// </summary>
    public Float3 SampleWind(Float3 worldPosition, float time)
    {
        Float3 toPoint = worldPosition - Transform.Position;
        float distance = Float3.Length(toPoint);

        float strength = StrengthAt(worldPosition, distance, time);
        if (strength == 0f) return Float3.Zero;

        Float3 direction = distance > 1e-4f ? toPoint / distance : Float3.UnitY;
        return direction * strength;
    }

    /// <summary>Force at a point that sits <paramref name="distance"/> from the center.</summary>
    public float StrengthAt(Float3 worldPosition, float distance, float time)
    {
        float radius = MathF.Max(Radius, 1e-4f);
        if (distance >= radius) return 0f;

        float t = 1f - distance / radius;
        float falloff = t * t * (3f - 2f * t);
        float pulse = 1f + MathF.Sin(time * PulseFrequency * MathF.Tau + distance * 0.1f) * PulseMagnitude;
        float turbulence = 1f + MathF.Sin(time * 3f + worldPosition.X * 0.7f + worldPosition.Z * 0.9f) * Turbulence;
        return WindMain * falloff * pulse * turbulence;
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
