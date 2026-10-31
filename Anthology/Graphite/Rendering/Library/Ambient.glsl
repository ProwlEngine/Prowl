// Ambient and baked light: the environment's ambient light, the sky's harmonics, light probes, lightmaps and the view's
// screen lighting, all pre-exposed unless they say otherwise.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Textures.glsl"
#include "Graphite/WorldLighting.glsl"

vec3 Graphite_SkyAmbient(vec3 worldNormal);

// The environment's ambient light along a normal, irradiance over pi in nits, pre-exposed
vec3 Graphite_AmbientLight(vec3 worldNormal)
{
    vec3 ambient = graphite_World.ambientColor.rgb * graphite_World.ambientMode.x;
    if (graphite_World.ambientMode.w > 0.0)
        ambient += Graphite_SkyAmbient(worldNormal) * graphite_World.ambientMode.w;

    float upDot = dot(worldNormal, vec3(0.0, 1.0, 0.0));
    ambient += mix(graphite_World.ambientGround.rgb, graphite_World.ambientSky.rgb, upDot * 0.5 + 0.5) * graphite_World.ambientMode.y;

    return ambient * Graphite_Exposure();
}

struct Graphite_ProbeSH { vec4 shAr; vec4 shAg; vec4 shAb; vec4 shBr; vec4 shBg; vec4 shBb; vec4 shC; };

layout(set = 1, binding = 23, std430) readonly buffer Graphite_ObjectProbes { Graphite_ProbeSH graphite_ObjectProbes[]; };

#define GRAPHITE_NO_LIGHTMAP 0xFFFFFFFFu
#define GRAPHITE_LIGHTMAP_LOADING 0xFFFFFFFEu

// A lightmap texel as light: as is, or colour times alpha times 8 for RGBM, times the lightmap's scale
vec3 Graphite_DecodeLightmap(vec4 texel, uint encoding, float scale)
{
    return (encoding == 1u ? texel.rgb * (texel.a * 8.0) : texel.rgb) * scale;
}

// Diffuse irradiance over pi for a normal from packed order 2 harmonics
vec3 Graphite_ShadeSH9(Graphite_ProbeSH sh, vec3 n)
{
    vec4 nrm = vec4(n, 1.0);
    vec3 x;
    x.r = dot(sh.shAr, nrm);
    x.g = dot(sh.shAg, nrm);
    x.b = dot(sh.shAb, nrm);

    vec4 vB = n.xyzz * n.yzzx;
    vec3 x1;
    x1.r = dot(sh.shBr, vB);
    x1.g = dot(sh.shBg, vB);
    x1.b = dot(sh.shBb, vB);

    float vC = n.x * n.x - n.y * n.y;
    vec3 x2 = sh.shC.rgb * vC;

    return max(x + x1 + x2, vec3(0.0));
}

layout(set = 1, binding = 26, std430) readonly buffer Graphite_SkyAmbientSH { Graphite_ProbeSH graphite_SkyAmbient; };

// The sky's light along a normal from its harmonics, not exposed
vec3 Graphite_SkyAmbient(vec3 worldNormal)
{
    return Graphite_ShadeSH9(graphite_SkyAmbient, worldNormal) * graphite_World.skyScale.x;
}

// The view's screen lighting, written by its ScreenLightingFeature after the prepass: visibility, and indirect irradiance
// with its confidence. Views without one read full visibility and no irradiance.
layout(set = 1, binding = 31) uniform texture2DArray Graphite_ScreenVisibility;
layout(set = 1, binding = 32) uniform texture2DArray Graphite_ScreenIrradiance;
layout(set = 1, binding = 33) uniform sampler Graphite_ScreenSampler;

#ifdef GRAPHITE_FRAGMENT
vec3 graphite_ScreenCoord() { return vec3(gl_FragCoord.xy * Graphite_View().screen.zw, float(gl_ViewIndex)); }

// How much indirect light reaches this pixel, from the view's screen lighting
float Graphite_ScreenOcclusion() { return texture(sampler2DArray(Graphite_ScreenVisibility, Graphite_ScreenSampler), graphite_ScreenCoord()).r; }

// Indirect irradiance the view's screen lighting found at this pixel, pre-exposed like the scene colour it comes from,
// and how far to trust it over the baked light
vec4 Graphite_ScreenIndirect() { return texture(sampler2DArray(Graphite_ScreenIrradiance, Graphite_ScreenSampler), graphite_ScreenCoord()); }
#endif

#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
// The diffuse light that is not realtime: the object's lightmap, ambient while it loads, else its light probes, else the
// ambient light, occluded by the view's screen lighting and blended toward the irradiance it found, pre-exposed.
// lightmapUV comes from Graphite_LightmapUV in the vertex stage.
vec3 graphite_BakedDiffuse(vec3 worldNormal, vec2 lightmapUV);

vec3 Graphite_IndirectDiffuse(vec3 worldNormal, vec2 lightmapUV)
{
    vec4 screen = Graphite_ScreenIndirect();
    return mix(graphite_BakedDiffuse(worldNormal, lightmapUV) * Graphite_ScreenOcclusion(), screen.rgb, screen.a);
}

vec3 graphite_BakedDiffuse(vec3 worldNormal, vec2 lightmapUV)
{
    Graphite_ObjectData object = Graphite_Object();
    if (object.lightmapTexture == GRAPHITE_LIGHTMAP_LOADING)
        return Graphite_AmbientLight(worldNormal) * graphite_World.ambientMode.z;
    if (object.lightmapTexture != GRAPHITE_NO_LIGHTMAP)
        return Graphite_DecodeLightmap(GRAPHITE_SAMPLE_2D(object.lightmapTexture, object.lightmapSampler & 0xFFFFu, lightmapUV), object.lightmapSampler >> 16, object.lightmapScale) * Graphite_Exposure();
    if (object.probeIndex != 0xFFFFFFFFu && graphite_World.counts.z != 0u)
        return Graphite_ShadeSH9(graphite_ObjectProbes[object.probeIndex], worldNormal) * Graphite_Exposure();
    return Graphite_AmbientLight(worldNormal) * graphite_World.ambientMode.z;
}
#endif
