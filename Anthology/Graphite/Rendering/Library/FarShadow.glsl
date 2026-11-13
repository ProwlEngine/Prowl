// The sun's shadow far past its cascades, from the world's far shadow map: a height field seen from above whose texels
// hold the height below which the sun is blocked there. Filled by whatever the host or a feature sets as
// RenderWorld.FarShadow, see Prowl.Graphite.Rendering.FarShadowMap.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Tables.glsl"

layout(set = 1, binding = 48) uniform texture2D Graphite_FarShadowMap;

struct Graphite_FarShadowRecord
{
    vec4 area;      // the corner with the lowest x and z, the metres it spans, 1 while on
    vec4 params;    // the softness and bias in metres, the distance it fades in from, 0 for everywhere
};

layout(set = 1, binding = 49, std430) readonly buffer Graphite_FarShadowData { Graphite_FarShadowRecord graphite_FarShadow; };

// How much of the sun reaches a camera relative point past the far shadow, 1 lit or where the map does not reach
float Graphite_FarShadow(vec3 relativePos)
{
    Graphite_FarShadowRecord r = graphite_FarShadow;
    if (r.area.w < 0.5)
        return 1.0;
    float away = length(relativePos);
    float fade = r.params.z > 0.0 ? smoothstep(r.params.z, r.params.z * 1.2, away) : 1.0;
    if (fade <= 0.0)
        return 1.0;

    Graphite_ViewData view = Graphite_View();
    vec3 world = view.cameraHigh.xyz + (view.cameraLow.xyz + relativePos);
    vec2 uv = (world.xz - r.area.xy) / r.area.z;
    vec2 edge = min(uv, 1.0 - uv);
    float inside = smoothstep(0.0, 0.05, min(edge.x, edge.y));
    if (inside <= 0.0)
        return 1.0;

    float height = textureLod(sampler2D(Graphite_FarShadowMap, Graphite_TableSampler), uv, 0.0).r;
    float lit = smoothstep(-r.params.x, r.params.x, world.y + r.params.y - height);
    return mix(1.0, lit, fade * inside);
}
