// Fog toward a relative position: linear, exponential or exponential squared, coloured flat or by the sky probe.
#pragma once

#include "Graphite/Probes.glsl"

// How blurred the sky is that fog takes its color from, as a sky probe roughness
#define GRAPHITE_FOG_SKY_ROUGHNESS 0.35

// The fog color seen toward relativePos, either the flat fog color in nits or the sky behind it from the sky probe, so
// it meets whatever sky is drawn seamlessly, pre-exposed
vec3 Graphite_FogColor(vec3 relativePos)
{
    if (graphite_World.fogStates.w < 0.5 || !Graphite_ReflectionProbesReady())
        return graphite_World.fogColor.rgb * Graphite_Exposure();

    // Below the horizon the fog keeps the horizon's color rather than the ground under the sky
    vec3 dir = relativePos;
    dir.y = max(dir.y, 0.0);
    dir = length(dir) > 1e-4 ? normalize(dir) : vec3(0.0, 0.0, 1.0);
    return Graphite_SampleSkyProbe(dir, GRAPHITE_FOG_SKY_ROUGHNESS);
}

vec3 Graphite_ApplyFog(vec3 color, vec3 relativePos)
{
    vec4 states = graphite_World.fogStates;
    if (states.x + states.y + states.z < 0.5)
        return color;

    vec4 params = graphite_World.fogParams;
    float fogCoord = length(relativePos);
    float fog = 0.0;
    fog += (fogCoord * params.z + params.w) * states.x;
    fog += exp2(-fogCoord * params.y) * states.y;
    fog += exp2(-fogCoord * fogCoord * params.x * params.x) * states.z;
    return mix(Graphite_FogColor(relativePos), color, clamp(fog, 0.0, 1.0));
}
