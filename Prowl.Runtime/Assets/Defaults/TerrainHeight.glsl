// Terrain height field sampling, shared by the surface and the detail scatter so both sit on the same
// ground. TerrainData.GetInterpolatedHeight mirrors this tap for tap on the CPU.
//
// Heights are a vertex grid, sample 0 sits on the terrain edge, so UV 0..1 spans samples 0..size-1.
// Taps are exact texel fetches clamped to the edge rather than filtered reads, since hardware filtering
// weights are low precision and the CPU could not match them.

uniform sampler2D _Heightmap;
uniform float _TerrainHeight;

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
