// The ocean's wave cascades as OceanFeature writes them, sampled at a point on the tiles. Positions are metres from the
// corner of the largest tile, which every smaller tile repeats inside. A cascade fades out with distance, from half
// its fade distance to all of it, and its mips filter waves shorter than what samples it, a pixel or a mesh quad.
// Waves shrink in water shallower than half their length, each cascade by a typical wavelength of a
// fifth of its tile, by the shallow attenuation's share, and every wave fades out over the last half metre to the shore. On FULL a cascade magnified past a texel a pixel is sampled
// with a cubic B-spline from four bilinear taps, so its slopes show no texel edges up close.
#pragma once

#include "Graphite/Core.glsl"

uniform sampler2DArray _OceanDisplacement;
uniform sampler2DArray _OceanSlopes;
uniform sampler2DArray _OceanFoam;
uniform sampler2DArray _OceanDepth;
uniform sampler2DArray _OceanLevels;
uniform sampler2DArray _OceanFlow;
uniform sampler2DArray _OceanClip;
uniform sampler2DArray _OceanWhitecaps;
uniform sampler2DArray _OceanRipples;
uniform sampler2D _OceanState;  // this frame's depth viewer xz modulo the largest depth texel, written with the slices
uniform vec4 _OceanSizes;       // each cascade's tile size in metres
uniform vec4 _OceanFade;        // the distance each cascade is gone by, in metres
uniform vec4 _OceanDepthParams; // unused, unused, the first slice's width, resolution
uniform vec4 _OceanShore;       // shallow attenuation, shoreline foam depth, shoreline foam strength, depth slices
uniform vec4 _OceanRippleParams; // how far ripples push sideways, the most they push as a share of a texel, ripple slices

#define OCEAN_CASCADES 4
#define OCEAN_DEEP 1000.0
#define OCEAN_FLOW_PERIOD 2.0

vec2 ocean_ViewerWrapped() { return texelFetch(_OceanState, ivec2(0), 0).xy; }

// Where a position relative to the depth viewer lands on a slice of the depth and the ripples
vec3 ocean_SliceUV(vec2 position, int slice)
{
    float width = _OceanDepthParams.z * exp2(float(slice));
    float texel = width / _OceanDepthParams.w;
    return vec3((position + mod(ocean_ViewerWrapped(), texel)) / width + 0.5, float(slice));
}

// A field on the depth's slices at a position relative to the depth viewer, from the finest slice holding it,
// blending into the next near its edge, outside past the last
vec4 ocean_SliceSample(sampler2DArray field, vec2 position, vec4 outside)
{
    float reach = max(abs(position.x), abs(position.y)) / (0.45 * _OceanDepthParams.z);
    int slices = int(_OceanShore.w);
    int slice = int(ceil(log2(max(reach, 1.0))));
    if (slice >= slices)
        return outside;
    vec4 value = textureLod(field, ocean_SliceUV(position, slice), 0.0);
    float edge = smoothstep(0.8, 1.0, reach / exp2(float(slice)));
    if (edge <= 0.0)
        return value;
    return mix(value, slice + 1 < slices ? textureLod(field, ocean_SliceUV(position, slice + 1), 0.0) : outside, edge);
}

// The current at a position relative to the depth viewer, in m/s along x and z
vec2 Ocean_Flow(vec2 position)
{
    return ocean_SliceSample(_OceanFlow, position, vec4(0.0)).xy;
}

// Two phase flow: the waves are read twice, each read sliding with the current and starting over each period
// half a period apart, the one nearer its start weighted more, so the waves drift with the current without stretching.
// Gives both offsets and the first one's weight.
float Ocean_FlowPhases(vec2 flow, out vec2 offset0, out vec2 offset1)
{
    float t = Graphite_View().time.x / OCEAN_FLOW_PERIOD;
    float phase0 = fract(t), phase1 = fract(t + 0.5);
    offset0 = -flow * (phase0 * OCEAN_FLOW_PERIOD);
    offset1 = -flow * (phase1 * OCEAN_FLOW_PERIOD);
    return 1.0 - abs(2.0 * phase0 - 1.0);
}

// Whether the surface is cut away where the waves land on a position relative to the depth viewer
bool Ocean_Clipped(vec2 landed) { return ocean_SliceSample(_OceanClip, landed, vec4(0.0)).r > 0.5; }

// The whitecaps the simulation holds at a position relative to the depth viewer, below 0 past its slices
float Ocean_Whitecaps(vec2 position) { return ocean_SliceSample(_OceanWhitecaps, position, vec4(-1.0)).r; }

bool Ocean_Flows(vec2 flow) { return dot(flow, flow) > 1e-6; }

// The water's level above sea level at a position relative to the depth viewer, a lake's or a river's
float Ocean_WaterLevel(vec2 position)
{
    return ocean_SliceSample(_OceanLevels, position, vec4(0.0)).r;
}

// The water's depth at a position relative to the depth viewer, from its level down to the floor
float Ocean_WaterDepth(vec2 position)
{
    return ocean_SliceSample(_OceanDepth, position, vec4(OCEAN_DEEP)).r + Ocean_WaterLevel(position);
}

// How much of a ripple slice reaches a position relative to the depth viewer, fading out near the slice's edge
float ocean_RippleWeight(vec2 position, int slice)
{
    float reach = max(abs(position.x), abs(position.y)) / (0.5 * _OceanDepthParams.z * exp2(float(slice)));
    return 1.0 - smoothstep(0.8, 0.95, reach);
}

// The ripples at a position relative to the depth viewer, (height, velocity, dh/dx, dh/dz), every ripple slice holding
// it added together, each slice simulating its own band
vec4 Ocean_Ripples(vec2 position)
{
    vec4 sum = vec4(0.0);
    for (int slice = 0; slice < int(_OceanRippleParams.z); slice++)
    {
        float weight = ocean_RippleWeight(position, slice);
        if (weight > 0.0)
            sum += textureLod(_OceanRipples, ocean_SliceUV(position, slice), 0.0) * weight;
    }
    return sum;
}

// How the ripples move the surface: their height, and sideways from their slope, each
// slice by its own texel
vec3 Ocean_RippleDisplacement(vec2 position)
{
    vec3 sum = vec3(0.0);
    for (int slice = 0; slice < int(_OceanRippleParams.z); slice++)
    {
        float weight = ocean_RippleWeight(position, slice);
        if (weight <= 0.0)
            continue;
        vec4 ripples = textureLod(_OceanRipples, ocean_SliceUV(position, slice), 0.0);
        float texel = _OceanDepthParams.z * exp2(float(slice)) / _OceanDepthParams.w;
        float k = 6.28318530718 / (3.0 * texel);
        vec2 sideways = clamp(_OceanRippleParams.x * ripples.zw / k, -_OceanRippleParams.y * texel, _OceanRippleParams.y * texel);
        sum += vec3(sideways.x, ripples.x, sideways.y) * weight;
    }
    return sum;
}

// The depth each cascade's waves feel
vec4 Ocean_CascadeDepths(vec2 position)
{
    return vec4(Ocean_WaterDepth(position));
}

float Ocean_CascadeWeight(int cascade, float far, vec4 depths)
{
    float fade = _OceanFade[cascade];
    float depth = depths[cascade];
    float shallow = clamp(2.0 * depth / (0.2 * _OceanSizes[cascade]), 0.0, 1.0);
    float shore = clamp(depth / 0.5, 0.0, 1.0);
    return (1.0 - smoothstep(0.5 * fade, fade, far)) * mix(1.0, shallow, _OceanShore.x) * shore;
}

// How much shoreline foam water of a depth has, 0 to 1, none under the floor
float Ocean_ShoreFoam(float depth)
{
    return clamp(_OceanShore.z * (1.0 - depth / _OceanShore.y), 0.0, 1.0) * smoothstep(-0.1, 0.0, depth);
}

vec3 Ocean_TileUV(vec2 position, int cascade)
{
    return vec3(position / _OceanSizes[cascade], float(cascade));
}

// The displacement carried along by a current, read at both flow phases where there is one
vec4 Ocean_FlowDisplacement(vec2 position, vec2 flow, float far, vec4 depths, float spacing);

// x, height, z and dDx/dz, summed over the cascades, filtered to a spacing in metres between samples
vec4 Ocean_Displacement(vec2 position, float far, vec4 depths, float spacing)
{
    float texels = float(textureSize(_OceanDisplacement, 0).x);
    vec4 sum = vec4(0.0);
    for (int i = 0; i < OCEAN_CASCADES; i++)
    {
        float weight = Ocean_CascadeWeight(i, far, depths);
        if (weight > 0.0)
            sum += textureLod(_OceanDisplacement, Ocean_TileUV(position, i), max(log2(spacing * texels / _OceanSizes[i]), 0.0)) * weight;
    }
    return sum;
}

vec4 Ocean_FlowDisplacement(vec2 position, vec2 flow, float far, vec4 depths, float spacing)
{
    if (!Ocean_Flows(flow))
        return Ocean_Displacement(position, far, depths, spacing);
    vec2 offset0, offset1;
    float weight = Ocean_FlowPhases(flow, offset0, offset1);
    return mix(Ocean_Displacement(position + offset1, far, depths, spacing), Ocean_Displacement(position + offset0, far, depths, spacing), weight);
}

#ifdef GRAPHITE_FRAGMENT
// A cascade's texel in the fragment stage, cubic where it is magnified, gx and gy the position's screen gradients,
// taken where control flow is uniform
vec4 Ocean_Fetch(sampler2DArray fields, vec2 position, int cascade, vec2 gx, vec2 gy)
{
    vec3 uv = Ocean_TileUV(position, cascade);
    vec2 size = vec2(textureSize(fields, 0).xy);
    vec2 dx = gx / _OceanSizes[cascade], dy = gy / _OceanSizes[cascade];
#ifndef PBR_TIER_LITE
    if (max(length(dx * size), length(dy * size)) < 1.0)
    {
        vec2 p = uv.xy * size - 0.5;
        vec2 i = floor(p), f = p - i;
        vec2 f2 = f * f, f3 = f2 * f;
        vec2 w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0;
        vec2 w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
        vec2 w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0;
        vec2 w3 = f3 / 6.0;
        vec2 g0 = w0 + w1, g1 = w2 + w3;
        vec2 h0 = (i - 0.5 + w1 / g0) / size, h1 = (i + 1.5 + w3 / g1) / size;
        return g0.y * (g0.x * textureLod(fields, vec3(h0.x, h0.y, uv.z), 0.0) + g1.x * textureLod(fields, vec3(h1.x, h0.y, uv.z), 0.0))
            + g1.y * (g0.x * textureLod(fields, vec3(h0.x, h1.y, uv.z), 0.0) + g1.x * textureLod(fields, vec3(h1.x, h1.y, uv.z), 0.0));
    }
#endif
    return textureGrad(fields, uv, dx, dy);
}

// What the fragment stage reads of the cascades at a point: the slopes (dh/dx, dh/dz, dDx/dx, dDz/dz), dDx/dz and foam
struct Ocean_Fields
{
    vec4 slopes;
    float twist;
    float foam;
};

Ocean_Fields Ocean_ReadFields(vec2 position, vec2 gx, vec2 gy, float far, vec4 depths)
{
    Ocean_Fields fields;
    fields.slopes = vec4(0.0);
    fields.twist = 0.0;
    fields.foam = 0.0;
    for (int i = 0; i < OCEAN_CASCADES; i++)
    {
        float weight = Ocean_CascadeWeight(i, far, depths);
        fields.slopes += Ocean_Fetch(_OceanSlopes, position, i, gx, gy) * weight;
        fields.twist += Ocean_Fetch(_OceanDisplacement, position, i, gx, gy).w * weight;
        fields.foam = max(fields.foam, textureGrad(_OceanFoam, Ocean_TileUV(position, i), gx / _OceanSizes[i], gy / _OceanSizes[i]).r * weight);
    }
    fields.foam = min(fields.foam, 1.0);
    return fields;
}

// The fields carried along by a current, read at both flow phases where there is one
Ocean_Fields Ocean_FlowFields(vec2 position, vec2 flow, vec2 gx, vec2 gy, float far, vec4 depths)
{
    if (!Ocean_Flows(flow))
        return Ocean_ReadFields(position, gx, gy, far, depths);
    vec2 offset0, offset1;
    float weight = Ocean_FlowPhases(flow, offset0, offset1);
    Ocean_Fields a = Ocean_ReadFields(position + offset0, gx, gy, far, depths);
    Ocean_Fields b = Ocean_ReadFields(position + offset1, gx, gy, far, depths);
    a.slopes = mix(b.slopes, a.slopes, weight);
    a.twist = mix(b.twist, a.twist, weight);
    a.foam = mix(b.foam, a.foam, weight);
    return a;
}
#endif
