// The built in skies' shared parts: the procedural sky's radiance and its sun. Sky shaders include Graphite/Sky.glsl first.
#pragma once

#include "Graphite/Lighting.glsl"

// The direction toward the sun: the world's sun light, else a default low in the sky
vec3 Graphite_SunDirection()
{
    return Graphite_DirectionalCount() > 0u ? Graphite_GetDirectional(0u).direction : normalize(vec3(-0.5, 0.7, -0.5));
}

// Procedural sky radiance along a unit world direction, sun is the unit direction toward the sun.
// sunGlow scales the glow around the sun, 0 turns it off.
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

vec3 Graphite_JodieReinhard(vec3 c)
{
    float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
    vec3 tc = c / (c + 1.0);
    return mix(c / (l + 1.0), tc, tc);
}

// The procedural sky as drawn: the scattered light, the sun disk and a darker ground below the horizon
vec3 Graphite_ProceduralSky(vec3 view, vec3 sun)
{
    vec3 color = Graphite_SkyRadiance(view, sun, 1.0);
    float sunTransmittance = exp(-dot(vec3(0.33), vec3(5.802e-6, 13.558e-6, 33.1e-6)) * 8000.0 / max(view.y, 0.02));
    float disk = smoothstep(0.99985, 0.99999, dot(view, sun)) * step(0.0, view.y);
    color += disk * 2000.0 * sunTransmittance * vec3(1.0, 0.9, 0.8);
    color = mix(color, color * 0.25, smoothstep(0.0, -0.02, view.y));
    return Graphite_JodieReinhard(color * 40.0);
}
