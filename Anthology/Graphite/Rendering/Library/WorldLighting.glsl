// The world's lighting state every lighting file reads: light and probe counts, the sky probe, the ambient light and fog.
#pragma once

layout(set = 1, binding = 22, std430) readonly buffer Graphite_WorldLighting
{
    uvec4 counts;            // directional lights, unused, probes present, unused
    uvec4 reflections;       // the sky probe's cube slot, the probes' sampler slot, the sky probe's mips, whether it is captured
    vec4 skyScale;           // one over the exposure the sky probe and its harmonics were captured at, unused
    vec4 ambientColor;
    vec4 ambientSky;
    vec4 ambientGround;
    vec4 ambientMode;        // uniform, hemisphere, strength, sky
    vec4 fogColor;
    vec4 fogParams;
    vec4 fogStates;          // linear, exponential, exponential squared, use sky
} graphite_World;
