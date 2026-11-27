// Volumetric fog as every shader reads it: the world's fog density at a point, and the view's fog grid, which holds for
// each cell the light the fog scatters toward the camera between the camera and that cell and how much of what lies
// behind the cell it lets through. The grid has a cell per 8 by 8 pixels and depth slices spaced exponentially from the
// first slice's start out to the fog's reach, by distance from the camera, a slice set per layer one after another.
// See Graphite/Internal/VolumetricFogLight.glsl and VolumetricFogIntegrate.glsl, which fill it.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/WorldLighting.glsl"
#include "Graphite/Tables.glsl"

layout(set = 1, binding = 43) uniform texture3D Graphite_FogVolume;

struct Graphite_FogVolumeRecord
{
    vec4 centreHigh;     // centre, shape
    vec4 centreLow;      // centre, density
    vec4 row0;           // world to its own space about the centre, then falloff
    vec4 row1;           // then the cone's cosine
    vec4 row2;           // then the cone's sine
    vec4 albedo;
};

layout(set = 1, binding = 44, std430) readonly buffer Graphite_FogVolumes { Graphite_FogVolumeRecord graphite_FogVolumes[]; };

bool Graphite_VolumetricFogOn() { return graphite_World.volumetricGrid.x > 0.5; }

// The slice coordinate of a distance from the camera, slice s spanning s to s + 1
float Graphite_FogSlice(float along)
{
    vec4 grid = graphite_World.volumetricGrid;
    return grid.z * log(max(along, 1e-4) / grid.w) / log(grid.y / grid.w);
}

// The distance from the camera of a slice coordinate
float Graphite_FogSliceDistance(float slice)
{
    vec4 grid = graphite_World.volumetricGrid;
    return grid.w * pow(grid.y / grid.w, slice / grid.z);
}

// One fog volume's density at a camera relative point, fading out over its falloff past its shape
float graphite_FogVolumeDensity(Graphite_FogVolumeRecord v, vec3 relativePos, Graphite_ViewData view)
{
    int shape = int(v.centreHigh.w);
    if (shape == 0)
        return v.centreLow.w;
    vec3 offset = relativePos + (view.cameraHigh.xyz - v.centreHigh.xyz) + (view.cameraLow.xyz - v.centreLow.xyz);
    vec3 p = vec3(dot(v.row0.xyz, offset), dot(v.row1.xyz, offset), dot(v.row2.xyz, offset));
    float edge;
    if (shape == 1)
    {
        vec3 d = abs(p) - vec3(1.0);
        edge = length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0);
    }
    else if (shape == 2)
        edge = length(p) - 1.0;
    else if (shape == 3)
    {
        vec2 d = vec2(length(p.xz) - 1.0, abs(p.y) - 1.0);
        edge = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
    }
    else
    {
        vec2 c = vec2(length(p.xz), p.y);
        edge = max(c.x * v.row1.w - c.y * v.row2.w, max(c.y - 1.0, -p.y));
    }
    return v.centreLow.w * clamp(1.0 - edge / v.row0.w, 0.0, 1.0);
}

// The fog's density at a camera relative point, how much of the light it takes out per metre, and the share of that
// it scatters per colour: the height fog thinning above its base, and every fog volume
float Graphite_FogDensity(vec3 relativePos, out vec3 albedo)
{
    Graphite_ViewData view = Graphite_View();
    vec4 density = graphite_World.volumetricDensity;
    float height = view.cameraHigh.y + view.cameraLow.y + relativePos.y - density.y;
    float total = density.x * exp(-density.z * max(height, 0.0));
    vec3 scattered = graphite_World.volumetricAlbedo.rgb * total;
    uint count = graphite_World.counts.y;
    for (uint i = 0u; i < count; i++)
    {
        Graphite_FogVolumeRecord v = graphite_FogVolumes[i];
        float d = graphite_FogVolumeDensity(v, relativePos, view);
        total += d;
        scattered += v.albedo.rgb * d;
    }
    albedo = total > 0.0 ? scattered / total : vec3(0.0);
    return total;
}

// The fog between the camera and a camera relative point: the light it scatters toward the camera, pre-exposed, and
// how much of what lies at the point it lets through. Past the fog's reach the whole grid's column counts.
vec4 Graphite_VolumetricFog(vec3 relativePos)
{
    if (!Graphite_VolumetricFogOn())
        return vec4(0.0, 0.0, 0.0, 1.0);
    ivec3 size = textureSize(sampler3D(Graphite_FogVolume, Graphite_TableSampler), 0);
    float slices = graphite_World.volumetricGrid.z;
    if (float(size.z) < slices)
        return vec4(0.0, 0.0, 0.0, 1.0);
    Graphite_ViewData view = Graphite_View();
    vec4 clip = view.viewProjection * vec4(relativePos, 1.0);
    vec2 uv = vec2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);
    float slice = Graphite_FogSlice(length(relativePos));
    float z = (float(GRAPHITE_VIEW_INDEX) * slices + clamp(slice - 0.5, 0.5, slices - 0.5)) / float(size.z);

    // A cubic B-spline across the cells from four filtered reads, so the grid's cells leave no steps
    vec2 texel = uv * view.screen.xy / 8.0 - 0.5;
    vec2 base = floor(texel), f = texel - base;
    vec2 f2 = f * f, f3 = f2 * f;
    vec2 w0 = (1.0 - 3.0 * f + 3.0 * f2 - f3) / 6.0, w1 = (4.0 - 6.0 * f2 + 3.0 * f3) / 6.0;
    vec2 w2 = (1.0 + 3.0 * f + 3.0 * f2 - 3.0 * f3) / 6.0, w3 = f3 / 6.0;
    vec2 g0 = w0 + w1, g1 = w2 + w3;
    vec2 p0 = (base - 0.5 + w1 / g0) / vec2(size.xy), p1 = (base + 1.5 + w3 / g1) / vec2(size.xy);
    vec4 fog = g0.y * (g0.x * textureLod(sampler3D(Graphite_FogVolume, Graphite_TableSampler), vec3(p0.x, p0.y, z), 0.0)
                     + g1.x * textureLod(sampler3D(Graphite_FogVolume, Graphite_TableSampler), vec3(p1.x, p0.y, z), 0.0))
             + g1.y * (g0.x * textureLod(sampler3D(Graphite_FogVolume, Graphite_TableSampler), vec3(p0.x, p1.y, z), 0.0)
                     + g1.x * textureLod(sampler3D(Graphite_FogVolume, Graphite_TableSampler), vec3(p1.x, p1.y, z), 0.0));
    return mix(vec4(0.0, 0.0, 0.0, 1.0), fog, clamp(slice, 0.0, 1.0));
}
