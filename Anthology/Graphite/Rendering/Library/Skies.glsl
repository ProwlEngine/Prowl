// The built in skies' shared parts: the procedural sky's radiance and its sun, rotation and panorama mapping, for the
// code of a Surface "Sky" shader. Skies give radiance in nits, which the Sky model pre-exposes.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Directional.glsl"

// The procedural sky's luminance in nits per lux of sun. With the sun 60 degrees up the sky then lights a level surface
// with a fifth of the sun's illuminance, a clear day's 20000 lux of sky under a 100000 lux sun.
#define GRAPHITE_SKY_NITS_PER_LUX 2.8

// The illuminance of a sun when the world has no directional light, a clear day's
#define GRAPHITE_DEFAULT_SUN_LUX 100000.0

// The direction toward the sun: the world's sun light, else a default low in the sky
vec3 Graphite_SunDirection()
{
    return Graphite_DirectionalCount() > 0u ? Graphite_GetDirectional(0u).direction : normalize(vec3(-0.5, 0.7, -0.5));
}

// The sun's colour times its illuminance in lux: the world's sun light, else a default white sun
vec3 Graphite_SunIlluminance()
{
    return Graphite_DirectionalCount() > 0u ? Graphite_GetDirectional(0u).color : vec3(GRAPHITE_DEFAULT_SUN_LUX);
}

// Procedural sky radiance along a unit world direction per unit of the sky's brightness, sun is the unit direction
// toward the sun. sunGlow scales the glow around the sun, 0 turns it off.
vec3 Graphite_SkyRadiance(vec3 view, vec3 sun, float sunGlow)
{
    float up = max(sun.y, 0.0), dep = max(-sun.y, 0.0) * 28.2;
    float mu = max(view.y, 0.0), nu = dot(view, sun);

    // Air along the sun's path and the view's, and the light left after each
    float sunAir = 1.0279 / (up + 0.027943);
    float viewAir = 1.0279 / (mu + 0.027943);
    vec3 sunT = exp(-vec3(0.088977, 0.16327, 0.31751) * sunAir);
    vec3 viewT = exp(-vec3(0.046745, 0.097582, 0.24505) * viewAir);

    // Single scattering from the average sunlight along the view, filtered by ozone
    vec3 h = vec3(0.026626, 0.055859, 0.13174) * (viewAir - sunAir);
    vec3 lit = 0.5 * (sunT + viewT) * inversesqrt(1.0 + 0.8839 * h * h);
    vec3 ozone = exp(-vec3(-0.0026066, 0.01647, 0.0064102) * sunAir);
    float spread = inversesqrt(1.6156 - 1.5692 * nu);
    float glow = 0.00010368 * sunGlow * spread * spread * spread;
    vec3 single = ozone * lit * viewAir * (vec3(0.001631, 0.0038803, 0.010984) * (1.0 + nu * nu) + glow);

    float shade = 0.0;
    if (dep > 0.0)
    {
        // Earth shadow growing out from opposite the sun, with a pink band along its edge
        float az = dot(view.xz, sun.xz * inversesqrt(max(dot(sun.xz, sun.xz), 1e-12)));
        float x = (mu + 0.1272 * dep * az) / (0.035 * dep + 1e-4);
        float sunlit = smoothstep(-2.1169, 2.1169, x);
        float strength = smoothstep(0.0, 1.0, dep * 3.0 - 1.5 * (1.0 + az));
        float band = strength / (1.0 + 0.49 * x * x);
        shade = strength * (1.0 - sunlit);

        // Twilight fade and colour shift
        vec3 depth = dep * (vec3(0.72954 + 0.20683 * dep) + vec3(0.15423, 0.11361, -0.012866));
        single *= exp(-depth) * (1.0 - shade) * (1.0 + band * vec3(0.0, -0.4, -0.2));
    }

    // Light that bounced more than once, levelling off along long views, lighter and purple inside the shadow
    vec3 bounce = vec3(0.031648, 0.033978, 0.03542) * (up + 0.022059) * exp(-(vec3(1.8692) + vec3(-0.0023863, 0.020262, 0.05909)) * dep);
    bounce *= 1.0 + shade * vec3(4.0, 1.0, 2.0);
    return (single + bounce * (1.0 - viewT)) * 1.7557;
}

// How bright the sun's disk is against the sky's brightness, bright enough to read as the sun without its true glare
#define GRAPHITE_SUN_DISK_BRIGHTNESS 4.0

// The sun's disk and the glow around it at a view direction, per unit of the sky's brightness: a soft edged disk sizeScale
// times half a degree across, darker and warmer toward its rim as the real sun is, a tight corona around it and a wide
// faint halo, all reddened by the air the sunlight crosses, so it sets orange
vec3 Graphite_SunDisk(vec3 view, vec3 sun, float sizeScale, float glowScale)
{
    float radius = 0.0087 * sizeScale;
    float r = length(cross(view, sun)) / radius;
    if (dot(view, sun) <= 0.0)
        return vec3(0.0);

    // Limb darkening, the rim's light from shallower and cooler gas
    float mu = sqrt(max(1.0 - min(r * r, 1.0), 0.0));
    vec3 limb = vec3(1.0) - vec3(0.45, 0.6, 0.75) * (1.0 - mu);
    float edge = 1.0 - smoothstep(0.92, 1.0, r);
    vec3 disk = limb * edge;

    // A corona hugging the rim and a halo fading out over many radii
    float outside = max(r - 1.0, 0.0);
    float corona = exp(-outside * 3.0) * 0.12;
    float halo = 0.02 / (1.0 + outside * outside * 0.05);
    vec3 light = disk + vec3(corona + halo) * glowScale * (1.0 - edge);

    // The sun seen through the air, as the sky's own scattering reddens it
    float sunAir = 1.0279 / (max(sun.y, 0.0) + 0.027943);
    vec3 through = exp(-vec3(0.088977, 0.16327, 0.31751) * sunAir);
    return light * through / max(Graphite_Luminance(through), 1e-3) * min(Graphite_Luminance(through) * 4.0, 1.0);
}

// The procedural sky in nits: the scattered light of a sun of sunLux, its colour times lux, times tint, the sun's disk
// scaled by sunSize (0 hides it) and its glow by sunGlow, the light at the horizon times ground below it, all times
// brightness. Captures leave the disk out while a directional light is the sun, which lights everything already.
vec3 Graphite_ProceduralSky(vec3 view, vec3 sun, vec3 sunLux, float sunSize, float sunGlow, float brightness, vec3 tint, vec3 ground)
{
    float lux = Graphite_Luminance(sunLux);
    vec3 color = Graphite_SkyRadiance(view, sun, sunGlow) * tint * (lux * GRAPHITE_SKY_NITS_PER_LUX);
    if (sunSize > 0.0 && !(Graphite_IsCapture() && Graphite_DirectionalCount() > 0u))
    {
        vec3 hue = sunLux / max(lux, 1e-6);
        color += Graphite_SunDisk(view, sun, sunSize, sunGlow) * hue * (lux * GRAPHITE_SKY_NITS_PER_LUX * GRAPHITE_SUN_DISK_BRIGHTNESS) * smoothstep(-0.01, 0.0, view.y);
    }
    color = mix(color, color * ground, smoothstep(0.0, -0.02, view.y));
    return color * brightness;
}

// A direction turned about the up axis by degrees, for skies that rotate their image
vec3 Graphite_RotateSkyY(vec3 dir, float degrees)
{
    float a = radians(degrees);
    float s = sin(a), c = cos(a);
    return vec3(c * dir.x - s * dir.z, dir.y, s * dir.x + c * dir.z);
}

// The texture coordinate of a direction in an equirectangular panorama: +Z at the centre, up at the top row
vec2 Graphite_PanoramaUV(vec3 dir)
{
    return vec2(atan(dir.x, dir.z) * GRAPHITE_INV_TWO_PI + 0.5, asin(clamp(dir.y, -1.0, 1.0)) * GRAPHITE_INV_PI + 0.5);
}
