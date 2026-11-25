// The GBuffer of a deferred view: what the GBuffer pass keeps of each visible opaque surface for the lighting pass.
// Its depth is the scene's, and its normal and motion the scene's own targets where the scene has them. The rest is
// a texture of its own.
//   Base      rgb base colour, a metalness
//   Normal    world normal, octahedral
//   Surface   r roughness, g specular, b occlusion, a the shading model, light layers and shadow flag in one byte
//   Custom    four values the shading model reads its own way
//   Baked     the diffuse light that is not realtime, pre-exposed
//   Emission  pre-exposed
//   Motion    as Graphite/Passes.glsl writes it
// specular is the dielectric reflectance at normal incidence over 0.08, so 0.5 is the 4% of an IOR of 1.5.
// A GBuffer pass defines GRAPHITE_GBUFFER_PASS and writes through Graphite_WriteGBuffer. A fullscreen program defines
// GRAPHITE_GBUFFER_READ and reads through Graphite_ReadGBuffer.
#pragma once

#include "Graphite/Octahedral.glsl"

// A pixel the GBuffer pass drew nothing on, as the GBuffer's clear leaves it
#define GRAPHITE_MODEL_NONE 0u
#define GRAPHITE_MODEL_LIT 1u
#define GRAPHITE_MODEL_UNLIT 2u
#define GRAPHITE_MODEL_FOLIAGE 3u

#define GRAPHITE_GBUFFER_SPECULAR_RANGE 0.08

struct Graphite_GBuffer
{
    vec3  baseColor;
    float metalness;
    vec3  normal;
    float roughness;
    float specular;
    float occlusion;
    uint  model;            // 0 to 15
    uint  lightLayers;      // the surface's layer mask, of which the GBuffer keeps the first 3 bits and reads the rest as set
    bool  receivesShadows;
    vec4  custom;
    vec3  baked;
    vec3  emission;
};

Graphite_GBuffer Graphite_NewGBuffer(uint model)
{
    Graphite_GBuffer g;
    g.baseColor = vec3(0.0);
    g.metalness = 0.0;
    g.normal = vec3(0.0, 1.0, 0.0);
    g.roughness = 1.0;
    g.specular = 0.5;
    g.occlusion = 1.0;
    g.model = model;
    g.lightLayers = 0xFFFFFFFFu;
    g.receivesShadows = true;
    g.custom = vec4(0.0);
    g.baked = vec3(0.0);
    g.emission = vec3(0.0);
    return g;
}

float Graphite_PackModel(Graphite_GBuffer g)
{
    return float((g.model & 15u) | (g.lightLayers & 7u) << 4 | (g.receivesShadows ? 128u : 0u)) / 255.0;
}

void Graphite_UnpackModel(float packed, inout Graphite_GBuffer g)
{
    uint bits = uint(packed * 255.0 + 0.5);
    g.model = bits & 15u;
    g.lightLayers = ((bits >> 4) & 7u) | 0xFFFFFFF8u;
    g.receivesShadows = (bits & 128u) != 0u;
}

#if defined(GRAPHITE_GBUFFER_PASS) && defined(GRAPHITE_FRAGMENT)
layout(location = 0) out vec4 graphite_GBufferBase;
layout(location = 1) out vec2 graphite_GBufferNormal;
layout(location = 2) out vec4 graphite_GBufferSurface;
layout(location = 3) out vec4 graphite_GBufferCustom;
layout(location = 4) out vec3 graphite_GBufferBaked;
layout(location = 5) out vec3 graphite_GBufferEmission;

void Graphite_WriteGBuffer(Graphite_GBuffer g)
{
    graphite_GBufferBase = vec4(clamp(g.baseColor, 0.0, 1.0), clamp(g.metalness, 0.0, 1.0));
    graphite_GBufferNormal = Graphite_OctEncode(normalize(g.normal));
    graphite_GBufferSurface = vec4(clamp(vec3(g.roughness, g.specular, g.occlusion), 0.0, 1.0), Graphite_PackModel(g));
    graphite_GBufferCustom = clamp(g.custom, 0.0, 1.0);
    graphite_GBufferBaked = max(g.baked, vec3(0.0));
    graphite_GBufferEmission = max(g.emission, vec3(0.0));
}
#endif

#ifdef GRAPHITE_GBUFFER_READ
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferBase;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferNormal;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferSurface;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferCustom;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferBaked;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferEmission;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferMotion;
uniform GRAPHITE_SCREEN_TEXTURE Graphite_GBufferDepth;

// The pixel's shading model alone, GRAPHITE_MODEL_NONE where the GBuffer holds no surface
uint Graphite_GBufferModelAt(ivec2 pixel) { return uint(Graphite_Fetch(Graphite_GBufferSurface, pixel, 0).a * 255.0 + 0.5) & 15u; }

// The scene's depth, reversed Z. Where the GBuffer holds no surface it is what the prepass drew there, or 0.
float Graphite_GBufferDepthAt(ivec2 pixel) { return Graphite_Fetch(Graphite_GBufferDepth, pixel, 0).r; }

vec2 Graphite_GBufferMotionAt(ivec2 pixel) { return Graphite_Fetch(Graphite_GBufferMotion, pixel, 0).rg; }

Graphite_GBuffer Graphite_ReadGBuffer(ivec2 pixel)
{
    Graphite_GBuffer g;
    vec4 base = Graphite_Fetch(Graphite_GBufferBase, pixel, 0);
    vec4 surface = Graphite_Fetch(Graphite_GBufferSurface, pixel, 0);
    g.baseColor = base.rgb;
    g.metalness = base.a;
    g.normal = Graphite_OctDecode(Graphite_Fetch(Graphite_GBufferNormal, pixel, 0).rg);
    g.roughness = surface.r;
    g.specular = surface.g;
    g.occlusion = surface.b;
    Graphite_UnpackModel(surface.a, g);
    g.custom = Graphite_Fetch(Graphite_GBufferCustom, pixel, 0);
    g.baked = Graphite_Fetch(Graphite_GBufferBaked, pixel, 0).rgb;
    g.emission = Graphite_Fetch(Graphite_GBufferEmission, pixel, 0).rgb;
    return g;
}
#endif
