// The world's lighting state every lighting file reads: light and probe counts, the sky probe, the ambient light and fog.
#pragma once

layout(set = 1, binding = 22, std430) readonly buffer Graphite_WorldLighting
{
    uvec4 counts;            // directional lights, fog volumes, probes present, unused
    uvec4 reflections;       // the sky probe's cube slot, the probes' sampler slot, the sky probe's mips, whether it is captured
    vec4 skyScale;           // one over the exposure the sky probe and its harmonics were captured at, unused
    vec4 ambientColor;
    vec4 ambientSky;
    vec4 ambientGround;
    vec4 ambientMode;        // uniform, hemisphere, strength, sky
    vec4 fogColor;
    vec4 fogParams;
    vec4 fogStates;          // linear, exponential, exponential squared, use sky
    vec4 volumetricGrid;     // volumetric fog on, its reach in metres, depth slices, the first slice's start
    vec4 volumetricDensity;  // density per metre at the base height, base height, height falloff, anisotropy
    vec4 volumetricAlbedo;   // albedo, ambient scale
} graphite_World;
