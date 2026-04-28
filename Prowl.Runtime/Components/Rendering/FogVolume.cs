// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Volumetric fog density region. Adds (or subtracts) fog density and color tint
/// in a region of space. Sampled per ray-march step by the volumetric fog effect.
/// </summary>
public enum FogVolumeShape
{
    /// <summary>Affects the entire scene uniformly. Equivalent to global fog.</summary>
    Global,
    /// <summary>Box defined by Transform.Scale (treated as half-extents).</summary>
    Box,
    /// <summary>Sphere with radius = Transform.Scale.X.</summary>
    Sphere,
    /// <summary>Cylinder along Y axis. Radius = Transform.Scale.X, height = Transform.Scale.Y * 2.</summary>
    Cylinder,
    /// <summary>Cone along +Y axis with apex at origin. Half-angle from <see cref="FogVolume.ConeAngle"/>,
    /// height = Transform.Scale.Y.</summary>
    Cone,
}

[AddComponentMenu("Rendering/Fog Volume")]
[ComponentIcon("\uf0c2")] // Cloud
public sealed class FogVolume : MonoBehaviour
{
    [SerializeField] public FogVolumeShape Shape = FogVolumeShape.Sphere;

    /// <summary>Density multiplier added on top of the effect's global density inside this volume.
    /// Negative values can carve fog out (clamped to 0 at the final sample).</summary>
    [SerializeField] public float DensityMultiplier = 1.0f;

    /// <summary>Color tint multiplied into scattering inside this volume.</summary>
    [SerializeField] public Color ColorTint = new(1f, 1f, 1f, 1f);

    /// <summary>Smooth falloff at the edges of the volume (0..1, fraction of the volume's extent).
    /// Ignored for Global. Higher = softer edges.</summary>
    [SerializeField] public float Falloff = 0.2f;

    /// <summary>Cone half-angle in degrees. Only used when <see cref="Shape"/> = Cone.</summary>
    [SerializeField] public float ConeAngle = 30f;

    public override void DrawGizmos()
    {
        var color = new Color(0.4f, 0.7f, 1f, 1f);

        switch (Shape)
        {
            case FogVolumeShape.Global:
                // Hint cube at the GO position
                Debug.DrawWireCube((Float3)Transform.Position, new Float3(0.5f, 0.5f, 0.5f), color);
                break;

            case FogVolumeShape.Box:
            {
                // Push the GO's full transform so the unit cube draws rotated + scaled
                Debug.PushMatrix(Transform.LocalToWorldMatrix);
                Debug.DrawWireCube(Float3.Zero, Float3.One, color);
                Debug.PopMatrix();
                break;
            }

            case FogVolumeShape.Sphere:
            {
                float r = (float)Transform.LossyScale.X;
                Debug.DrawWireSphere((Float3)Transform.Position, r, color);
                break;
            }

            case FogVolumeShape.Cylinder:
            {
                float r = (float)Transform.LossyScale.X;
                float h = (float)Transform.LossyScale.Y * 2f;
                Debug.DrawWireCylinder((Float3)Transform.Position, Transform.Rotation, r, h, color);
                break;
            }

            case FogVolumeShape.Cone:
            {
                float h = (float)Transform.LossyScale.Y;
                float r = h * (float)Math.Tan(ConeAngle * Math.PI / 180.0);
                Float3 apex = (Float3)Transform.Position;
                Float3 dir = Transform.Up * h;
                Debug.DrawWireCone(apex, dir, r, color);
                break;
            }
        }
    }
}
