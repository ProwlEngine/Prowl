// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// What carries values saved before Prowl's light was physical onto lux, candela and nits. They were drawn at an exposure
/// of 1, lights at 8 times their intensity. The factors keep their look under a camera at <see cref="EV100"/>, the
/// default, where an old sun of 0.6 lands near the 100000 lux of a real one.
/// </summary>
public static class LegacyLightUnits
{
    /// <summary>The exposure value old content is converted for.</summary>
    public const float EV100 = 14f;

    /// <summary>Old emission, ambient and fog brightness to nits: one over the exposure EV100 gives, 1.2 times 2 to the EV100.</summary>
    public const float Luminance = 1.2f * 16384f;

    /// <summary>Old light intensities to lux, candela or nits.</summary>
    public const float Light = 8f * Luminance;
}
