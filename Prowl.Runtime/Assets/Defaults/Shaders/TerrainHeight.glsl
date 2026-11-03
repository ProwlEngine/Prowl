// Terrain height field sampling, shared by the surface and the detail scatter so both sit on the same
// ground. TerrainData.GetInterpolatedHeight mirrors this tap for tap on the CPU.
//
// Heights are a vertex grid, sample 0 sits on the terrain edge, so UV 0..1 spans samples 0..size-1.
// Texture row z holds grid row z. Taps are exact texel fetches clamped to the edge rather than filtered
// reads, since hardware filtering weights are low precision and the CPU could not match them.
#pragma once

#pragma variant TERRAIN_BICUBIC

uniform sampler2D _Heightmap;
uniform float _TerrainHeight;
uniform float _TerrainSize;

float terrainHeightTap(int x, int z, int last)
{
    return texelFetch(_Heightmap, ivec2(clamp(x, 0, last), clamp(z, 0, last)), 0).r;
}

#ifdef TERRAIN_BICUBIC
vec4 terrainCatmullRom(float f)
{
    float f2 = f * f;
    float f3 = f2 * f;
    return vec4(
        -0.5 * f3 + f2 - 0.5 * f,
         1.5 * f3 - 2.5 * f2 + 1.0,
        -1.5 * f3 + 2.0 * f2 + 0.5 * f,
         0.5 * f3 - 0.5 * f2);
}

float terrainHeightRow(int x, int z, int last, vec4 w)
{
    return w.x * terrainHeightTap(x - 1, z, last) + w.y * terrainHeightTap(x, z, last)
         + w.z * terrainHeightTap(x + 1, z, last) + w.w * terrainHeightTap(x + 2, z, last);
}

// Catmull Rom over the 4x4 samples around the point. Passes through every sample.
float terrainHeight(vec2 uv)
{
    int last = textureSize(_Heightmap, 0).x - 1;
    vec2 p = uv * float(last);
    vec2 base = floor(p);
    ivec2 i = ivec2(base);
    vec4 wx = terrainCatmullRom(p.x - base.x);
    vec4 wz = terrainCatmullRom(p.y - base.y);

    float h = wz.x * terrainHeightRow(i.x, i.y - 1, last, wx) + wz.y * terrainHeightRow(i.x, i.y, last, wx)
            + wz.z * terrainHeightRow(i.x, i.y + 1, last, wx) + wz.w * terrainHeightRow(i.x, i.y + 2, last, wx);
    return h * _TerrainHeight;
}
#else
float terrainHeight(vec2 uv)
{
    int last = textureSize(_Heightmap, 0).x - 1;
    vec2 p = uv * float(last);
    vec2 base = floor(p);
    vec2 f = p - base;
    ivec2 i = ivec2(base);

    float h00 = terrainHeightTap(i.x, i.y, last);
    float h10 = terrainHeightTap(i.x + 1, i.y, last);
    float h01 = terrainHeightTap(i.x, i.y + 1, last);
    float h11 = terrainHeightTap(i.x + 1, i.y + 1, last);
    float row0 = h00 + (h10 - h00) * f.x;
    float row1 = h01 + (h11 - h01) * f.x;
    return (row0 + (row1 - row0) * f.y) * _TerrainHeight;
}
#endif

// Terrain local normal from central differences one heightmap sample apart
vec3 terrainNormal(vec2 uv)
{
    float hmSize = float(textureSize(_Heightmap, 0).x);
    float texelStep = hmSize > 1.0 ? 1.0 / (hmSize - 1.0) : 0.001;

    float hR = terrainHeight(uv + vec2(texelStep, 0.0));
    float hL = terrainHeight(uv - vec2(texelStep, 0.0));
    float hU = terrainHeight(uv + vec2(0.0, texelStep));
    float hD = terrainHeight(uv - vec2(0.0, texelStep));

    float worldStep = texelStep * _TerrainSize * 2.0;
    return normalize(vec3(-(hR - hL) / worldStep, 1.0, -(hU - hD) / worldStep));
}

#ifdef GRAPHITE_VERTEX
#include "Graphite/Vertex.glsl"

// A chunk vertex on the height field, in terrain local space. The chunk's draw item holds its terrain local corner in
// custom0.xy and its size in custom0.z.
vec3 terrainVertex(vec3 vertex, out vec2 terrainUV)
{
    vec4 chunk = Graphite_Object().custom0;
    vec2 localXZ = chunk.xy + vertex.xz * chunk.z;
    terrainUV = localXZ / _TerrainSize;
    return vec3(localXZ.x, terrainHeight(terrainUV), localXZ.y);
}
#endif
