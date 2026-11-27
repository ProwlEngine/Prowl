// The world's directional lights, the sun first, in lux. Skies read the sun from here without the rest of the lighting.
#pragma once

#include "Graphite/WorldLighting.glsl"

struct Graphite_DirectionalRecord
{
    vec4 directionShadow;    // surface to light direction, shadow enabled
    vec4 color;              // colour times intensity, layer mask as uint bits
    vec4 params;             // sin and cos of the angular radius, unused
    vec4 hostData;
    vec4 shadow;             // shadow depth and normal bias, strength, filter
};

layout(set = 1, binding = 27, std430) readonly buffer Graphite_Directionals { Graphite_DirectionalRecord graphite_Directionals[]; };

struct Graphite_DirectionalLight
{
    vec3  direction;         // surface to light
    vec3  color;             // colour times intensity
    float sinRadius;         // of the disk's angular radius
    float cosRadius;
    bool  shadow;
    uint  layers;
    vec4  hostData;
    float shadowDepthBias;
    float shadowNormalBias;
    float shadowStrength;
    float shadowFilter;
};

uint Graphite_DirectionalCount() { return graphite_World.counts.x; }

// The world's directional lights, the sun at 0
Graphite_DirectionalLight Graphite_GetDirectional(uint index)
{
    Graphite_DirectionalRecord r = graphite_Directionals[index];
    Graphite_DirectionalLight d;
    d.direction = normalize(r.directionShadow.xyz);
    d.color = r.color.rgb;
    d.sinRadius = r.params.x;
    d.cosRadius = r.params.y;
    d.shadow = r.directionShadow.w > 0.5;
    d.layers = floatBitsToUint(r.color.w);
    d.hostData = r.hostData;
    d.shadowDepthBias = r.shadow.x;
    d.shadowNormalBias = r.shadow.y;
    d.shadowStrength = r.shadow.z;
    d.shadowFilter = r.shadow.w;
    return d;
}
