// Light at voxel hits: the sun through its shadow over the box, emission, and the sky for rays that leave it, all
// pre-exposed by the voxel header's emission exposure. Programs bind the world's voxel lighting record, sun shadow and
// sky capture by name, see VoxelLightingRecord.
#pragma once

#include "Graphite/VoxelTrace.glsl"

struct Graphite_VoxelLightingData
{
    mat4 sunShadow;     // voxel coordinates to the sun shadow's clip space
    vec4 sunDirection;  // toward the sun, whether it casts a shadow
    vec4 sunColor;      // illuminance, whether there is a sun
    vec4 sky;           // the sky's colour without a capture, the capture's mip to read or -1 without one
};

layout(std430) readonly buffer Graphite_VoxelLighting { Graphite_VoxelLightingData graphite_VoxelLight; };
uniform sampler2DShadow Graphite_VoxelSunShadow;
uniform samplerCube Graphite_VoxelSky;

// How much of the sun reaches a point in voxel units, taken half a voxel off its surface. Outside the map is lit.
float Graphite_VoxelSunVisibility(vec3 position, vec3 normal)
{
    if (graphite_VoxelLight.sunDirection.w == 0.0)
        return 1.0;
    vec4 clip = graphite_VoxelLight.sunShadow * vec4(position + normal * 0.5, 1.0);
    vec3 ndc = clip.xyz / clip.w;
    vec2 uv = vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0))))
        return 1.0;
    return texture(Graphite_VoxelSunShadow, vec3(uv, clamp(ndc.z, 0.0, 1.0)));
}

// How much of the sky a point past what the voxels hold sees, from the sun's shadow there: the shadow stands in for
// the roofs above only while the sun is up and shadowed, fading out toward the horizon
float Graphite_VoxelSkyReach(float sunShadow)
{
    if (graphite_VoxelLight.sunColor.w == 0.0 || graphite_VoxelLight.sunDirection.w == 0.0)
        return 1.0;
    return mix(1.0, sunShadow, clamp(graphite_VoxelLight.sunDirection.y / 0.25, 0.0, 1.0));
}

// The sky seen along a direction, a blurred mip of its capture or its colour without one
vec3 Graphite_VoxelSkyRadiance(vec3 direction)
{
    vec4 sky = graphite_VoxelLight.sky;
    return sky.w < 0.0 ? sky.rgb : textureLod(Graphite_VoxelSky, direction, sky.w).rgb;
}

// Where a ray from origin in direction, in voxel units, leaves the box, in voxels along it
float Graphite_VoxelBoxExit(vec3 origin, vec3 direction)
{
    vec3 safe = mix(vec3(1e-6), direction, greaterThan(abs(direction), vec3(1e-6)));
    vec3 exits = (step(vec3(0.0), direction) * vec3(Graphite_VoxelResolution()) - origin) / safe;
    return max(min(exits.x, min(exits.y, exits.z)), 0.0);
}

// The sky a ray from origin in direction, in voxel units, finds leaving the box, as far as it reaches where it leaves
vec3 Graphite_VoxelSkyLeaving(vec3 origin, vec3 direction, out float reach)
{
    vec3 exit = origin + direction * Graphite_VoxelBoxExit(origin, direction);
    reach = Graphite_VoxelSkyReach(Graphite_VoxelSunVisibility(exit, graphite_VoxelLight.sunDirection.xyz));
    return Graphite_VoxelSkyRadiance(direction) * reach;
}

// Light from the voxels, pre-exposed by the emission exposure, at a view's exposure instead
vec3 Graphite_VoxelToView(vec3 light, float viewExposure) { return light * (viewExposure / graphite_Voxel.size.z); }

// The light a hit voxel sends back along the ray traced from origin in direction, in voxel units
vec3 Graphite_VoxelHitRadiance(Graphite_VoxelHit hit, vec3 origin, vec3 direction)
{
    uint word = Graphite_VoxelColorWord(hit.voxel);
    if (Graphite_VoxelIsEmissive(word))
        return Graphite_VoxelEmission(word);
    float NdotL = max(dot(hit.normal, graphite_VoxelLight.sunDirection.xyz), 0.0);
    if (NdotL == 0.0 || graphite_VoxelLight.sunColor.w == 0.0)
        return vec3(0.0);
    float visibility = Graphite_VoxelSunVisibility(origin + direction * hit.t, hit.normal);
    return Graphite_VoxelAlbedo(word) * graphite_VoxelLight.sunColor.rgb * (NdotL * visibility * 0.31830989);
}
