// The water as the ocean's surface draws it, for compute shaders. The including shader declares a push block with
// sizes (each cascade's tile size), depth (the depth viewer's xz modulo the largest depth texel, the first slice's
// width, resolution) and shore (shallow attenuation, depth slices, unused, cascade resolution).
#pragma once

uniform sampler2DArray Ocean_Displacement;
uniform sampler2DArray Ocean_Depth;
uniform sampler2DArray Ocean_Levels;

// The depth below the water's level and that level above the sea, at a position relative to the depth's viewer
vec2 OceanWater_Floor(vec2 position)
{
    float reach = max(abs(position.x), abs(position.y)) / (0.45 * push.depth.z);
    int slice = int(ceil(log2(max(reach, 1.0))));
    if (slice >= int(push.shore.y))
        return vec2(1000.0, 0.0);
    float width = push.depth.z * exp2(float(slice));
    float texel = width / push.depth.w;
    vec3 uv = vec3((position + mod(push.depth.xy, texel)) / width + 0.5, float(slice));
    float level = textureLod(Ocean_Levels, uv, 0.0).r;
    return vec2(textureLod(Ocean_Depth, uv, 0.0).r + level, level);
}

float OceanWater_Weight(int cascade, vec2 bed)
{
    float depth = OceanWater_Floor(bed).x;
    return mix(1.0, clamp(2.0 * depth / (0.2 * push.sizes[cascade]), 0.0, 1.0), push.shore.x) * clamp(depth / 0.5, 0.0, 1.0);
}

// The cascades summed at a point on the largest tile, waves shorter than span metres filtered out
vec4 OceanWater_Sample(sampler2DArray fields, vec2 position, vec2 bed, float span)
{
    vec4 sum = vec4(0.0);
    for (int i = 0; i < 4; i++)
    {
        float lod = max(log2(0.25 * span * push.shore.w / push.sizes[i]), 0.0);
        sum += textureLod(fields, vec3(position / push.sizes[i], float(i)), lod) * OceanWater_Weight(i, bed);
    }
    return sum;
}

// The point on the largest tile whose water lands on a point, by four fixed point steps
vec2 OceanWater_Source(vec2 wave, vec2 bed, float span)
{
    vec2 source = wave;
    for (int step = 0; step < 4; step++)
        source -= source + OceanWater_Sample(Ocean_Displacement, source, bed, span).xz - wave;
    return source;
}
