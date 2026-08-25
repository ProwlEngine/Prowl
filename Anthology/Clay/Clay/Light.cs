// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>Kind of punctual light.</summary>
public enum LightType
{
    /// <summary>Infinitely distant, all rays parallel to the node's forward axis.</summary>
    Directional,

    /// <summary>Emits from the node's position in every direction.</summary>
    Point,

    /// <summary>Emits from the node's position within a cone about its forward axis.</summary>
    Spot,
}

/// <summary>
/// A punctual light defined by the source file (glTF <c>KHR_lights_punctual</c>). Position and
/// orientation come from the <see cref="ModelNode"/> that references it.
/// </summary>
/// <remarks>
/// A directional or spot light aims along its node's forward axis, which is -Z in the source and
/// +Z after <c>ConvertCoordinateSystem</c>, matching the camera convention.
/// <para>
/// <see cref="Intensity"/> is carried in the source's photometric units, which are lux for
/// directional lights and candela for point and spot. Engines that use an arbitrary intensity scale
/// will want to rescale it rather than take it literally.
/// </para>
/// </remarks>
public sealed class Light
{
    /// <summary>Light name, or <c>null</c> when the source did not name it.</summary>
    public string? Name { get; init; }

    /// <summary>Which kind of light this is.</summary>
    public required LightType Type { get; init; }

    /// <summary>Linear RGB colour. Alpha is unused and always 1.</summary>
    public Color Color { get; init; } = new(1f, 1f, 1f, 1f);

    /// <summary>Brightness in lux (directional) or candela (point and spot).</summary>
    public float Intensity { get; init; } = 1f;

    /// <summary>
    /// Distance at which the light stops contributing, or <c>null</c> for unlimited. Point and spot
    /// only; a directional light has no range.
    /// </summary>
    public float? Range { get; init; }

    /// <summary>Angle in radians from the cone axis where falloff begins. Spot only.</summary>
    public float InnerConeAngleRadians { get; init; }

    /// <summary>Angle in radians from the cone axis where the light reaches zero. Spot only.</summary>
    public float OuterConeAngleRadians { get; init; } = MathF.PI / 4f;
}
