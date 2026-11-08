// The clouds' shadow on the sun, from the world's cloud shadow map while clouds draw: cascades side by side, each square
// to the sun around where a camera was when it was drawn, the smallest first. Each texel holds where its ray from the sun
// first meets cloud, the cloud's mean density below the shell under that, how much cloud the ray crosses in all and how
// much of it is in the shell. Filled by VolumetricCloudsFeature.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Tables.glsl"

layout(set = 1, binding = 45) uniform texture2D Graphite_CloudShadowMap;

struct Graphite_CloudShadowRecord
{
    vec4 cascades[4];    // centre, absolute, and half width across the sun
    vec4 heights;        // each cascade's half height toward the sun's side of the sky
    vec4 windA;          // how far the wind has carried the clouds since each cascade was drawn
    vec4 windB;
    vec4 right[4];       // each cascade's axes as it was drawn: across the map square to the sun
    vec4 up[4];
    vec4 toSun[4];       // and toward the sun
    vec4 params;         // texels along a cascade's side, 1 while clouds shade the sun, the clouds' extinction per metre of density
    vec4 scatter;        // how strong each further order of scattering is, how much of the cloud it still crosses
};

layout(set = 1, binding = 46, std430) readonly buffer Graphite_CloudShadowData { Graphite_CloudShadowRecord graphite_CloudShadow; };

// How deep the shell under where the map's rays first meet cloud is
#define GRAPHITE_CLOUD_SHELL 200.0

// How much cloud the sunlight crosses to reach a point relative to a cascade's centre, past 1 in reach outside it
float graphite_CloudShadowCascade(int c, vec3 rel, out float reach)
{
    Graphite_CloudShadowRecord r = graphite_CloudShadow;
    vec4 cascade = r.cascades[c];
    vec2 wind = c == 0 ? r.windA.xy : c == 1 ? r.windA.zw : c == 2 ? r.windB.xy : r.windB.zw;
    float resolution = r.params.x;
    vec3 toSun = r.toSun[c].xyz;
    rel -= vec3(wind.x, 0.0, wind.y);
    rel += toSun * (4.0 * cascade.w / resolution * max(toSun.y, 0.0));
    vec2 local = vec2(dot(rel, r.right[c].xyz) / cascade.w, dot(rel, r.up[c].xyz) / r.heights[c]);
    float along = dot(rel, toSun);
    reach = max(max(abs(local.x), abs(local.y)), abs(along) / min(6.0 * cascade.w, 80000.0)) / 0.98;
    if (reach >= 1.0)
        return 0.0;
    // Every read is held half a texel inside the cascade's own tile, so filtering never takes in its neighbour's
    vec2 tileMin = vec2(float(c) * 0.25 + 0.125 / resolution, 0.5 / resolution), tileMax = vec2(float(c + 1) * 0.25 - 0.125 / resolution, 1.0 - 0.5 / resolution);
    vec2 uv = vec2((local.x * 0.5 + 0.5 + float(c)) * 0.25, local.y * 0.5 + 0.5);
    vec2 spread = vec2(0.25, 1.0) / resolution;
    float depth = 0.0;
    for (int k = 0; k < 4; k++)
    {
        vec4 texel = textureLod(sampler2D(Graphite_CloudShadowMap, Graphite_TableSampler), clamp(uv + vec2(k & 1, k >> 1) * 2.0 * spread - spread, tileMin, tileMax), 0.0);
        float d = texel.x - along;
        depth += clamp(d <= GRAPHITE_CLOUD_SHELL ? texel.w * d / GRAPHITE_CLOUD_SHELL : texel.w + (d - GRAPHITE_CLOUD_SHELL) * texel.y, 0.0, texel.z);
    }
    return depth * 0.25;
}

// How much of the sun reaches a camera relative point past the clouds, 1 without clouds. The light scattered on through
// the cloud keeps some of it under thin cloud, and the little left under thick cloud is dropped, so it hides the sun's
// glints and highlights as it does the sun
float Graphite_CloudShadow(vec3 relativePos)
{
    if (graphite_CloudShadow.params.y < 0.5)
        return 1.0;
    Graphite_ViewData view = Graphite_View();
    float depth = 0.0;
    for (int c = 0; c < 4; c++)
    {
        vec3 rel = (view.cameraHigh.xyz - graphite_CloudShadow.cascades[c].xyz) + view.cameraLow.xyz + relativePos;
        float reach;
        float cascade = graphite_CloudShadowCascade(c, rel, reach);
        if (reach >= 1.0)
            continue;
        float fade = smoothstep(0.8, 1.0, reach);
        // The outermost cascade fades to no cloud over its edge
        if (c == 3)
            cascade *= 1.0 - fade;
        if (fade > 0.0 && c < 3)
        {
            vec3 next = (view.cameraHigh.xyz - graphite_CloudShadow.cascades[c + 1].xyz) + view.cameraLow.xyz + relativePos;
            float outer;
            float far = graphite_CloudShadowCascade(c + 1, next, outer);
            if (outer < 1.0)
                cascade = mix(cascade, far, fade);
        }
        depth = cascade;
        break;
    }
    float tau = depth * graphite_CloudShadow.params.z;
    vec2 scatter = graphite_CloudShadow.scatter.xy;
    vec2 strength = vec2(scatter.x, scatter.x * scatter.x), crossed = vec2(scatter.y, scatter.y * scatter.y);
    float through = (exp(-tau) + strength.x * exp(-tau * crossed.x) + strength.y * exp(-tau * crossed.y)) / (1.0 + strength.x + strength.y);
    return through * smoothstep(0.02, 0.3, through);
}
