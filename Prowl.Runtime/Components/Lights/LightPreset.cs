// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>A unit a light's intensity can be given in. <see cref="Light.ToStored"/> turns it into the one the light keeps.</summary>
public enum LightUnit
{
    /// <summary>Illuminance on a surface facing the light, what a directional light keeps.</summary>
    Lux,

    /// <summary>The exposure a white card in the light needs, 2.5 times 2 to it in lux.</summary>
    Ev100,

    /// <summary>All the light it puts out, the number on a bulb box.</summary>
    Lumen,

    /// <summary>Brightness in its brightest direction, what a point or spot keeps.</summary>
    Candela,

    /// <summary>How bright its surface looks, what a rect, disk or tube keeps.</summary>
    Nit,
}

/// <summary>
/// A real light to start from: its brightness in a unit, its colour temperature and, for spots and area lights, its
/// shape. <see cref="Light.ApplyPreset"/> sets them, a shape left null stays as it is.
/// </summary>
public sealed record LightPreset(string Name, LightUnit Unit, float Amount, float Kelvin,
    float? SpotAngle = null, float? Width = null, float? Height = null, float? Length = null, float? Radius = null);
