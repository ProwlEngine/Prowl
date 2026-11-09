// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>The shape a <see cref="FogVolume"/> fills.</summary>
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
public sealed class FogVolume : Component
{
    [SerializeField] public FogVolumeShape Shape = FogVolumeShape.Sphere;

    /// <summary>The density it adds inside, a multiple of the scene's volumetric fog density.</summary>
    [SerializeField] public float DensityMultiplier = 1.0f;

    /// <summary>The share of the light it takes out that it scatters, per colour.</summary>
    [SerializeField] public Color ColorTint = new(1f, 1f, 1f, 1f);

    /// <summary>Smooth falloff at the edges of the volume (0..1, fraction of the volume's extent).
    /// Ignored for Global. Higher = softer edges.</summary>
    [SerializeField] public float Falloff = 0.2f;

    /// <summary>Cone half-angle in degrees. Only used when <see cref="Shape"/> = Cone.</summary>
    [SerializeField] public float ConeAngle = 30f;

    /// <summary>The volume as the world's volumetric fog reads it, its density a multiple of the scene's.</summary>
    internal Graphite.Rendering.FogVolumeDesc Describe(float density)
    {
        Float4x4 m = Transform.LocalToWorldMatrix;
        return new Graphite.Rendering.FogVolumeDesc
        {
            Shape = (Graphite.Rendering.FogVolumeShape)Shape,
            Transform = (Double4x4)m,
            Density = Math.Max(DensityMultiplier * density, 0f),
            Falloff = Math.Clamp(Falloff, 0.001f, 1f),
            Albedo = ColorTint.ToFloat4(),
            ConeAngle = ConeAngle,
        };
    }

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
