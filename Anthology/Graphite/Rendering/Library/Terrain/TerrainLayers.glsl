// The terrain's material layers, shared by the pixel shader's direct path and the cache compositor: control samples
// give each layer a weight, the four strongest are sampled in world space, top down on gentle ground and from the sides
// on steep ground, and blended by their heights, so stones poke through grass instead of fading into it. Control texel
// bits: base layer 0 to 5, overlay layer 6 to 11, overlay weight 12 to 19.
#pragma once

#define TERRAIN_PAGE_SIZE 131.0

uniform sampler2DArray _TerrainControl;
uniform sampler2DArray _TerrainAlbedo;      // per layer: albedo, and height in alpha
uniform sampler2DArray _TerrainSurface;     // per layer: normal x and y, roughness, occlusion
uniform sampler2D _TerrainLayerData;        // per layer a texel: one over its tile size in metres, its height blend, 1 for hex tiling, macro variation
uniform vec4 _TerrainSpacing;               // the finest level's spacing in metres, the layer count

struct Terrain_Material
{
    vec3 albedo;
    vec3 normal;
    float roughness;
    float occlusion;
    int strongest;      // the layer with the most weight before the height blend
    vec4 weights;       // the four layers' blended weights
};

Terrain_Material Terrain_PlainMaterial(vec3 normal)
{
    Terrain_Material m;
    m.albedo = vec3(0.5);
    m.normal = normal;
    m.roughness = 0.8;
    m.occlusion = 1.0;
    m.strongest = 0;
    m.weights = vec4(1.0, 0.0, 0.0, 0.0);
    return m;
}

// Adds weight to a layer, taking the place of the weakest of the four when it is new and stronger
void terrain_AddLayer(inout ivec4 ids, inout vec4 weights, int id, float weight)
{
    if (weight <= 0.0)
        return;
    int weakest = 0;
    for (int k = 0; k < 4; k++)
    {
        if (ids[k] == id)
        {
            weights[k] += weight;
            return;
        }
        if (weights[k] < weights[weakest])
            weakest = k;
    }
    if (weights[weakest] < weight)
    {
        ids[weakest] = id;
        weights[weakest] = weight;
    }
}

// The bilinear share of each of a page's four control samples around a point of its uv in the pools, times scale
void Terrain_Gather(vec3 page, float scale, inout ivec4 ids, inout vec4 weights)
{
    if (scale <= 0.0)
        return;
    vec2 t = page.xy * TERRAIN_PAGE_SIZE - 0.5;
    vec2 base = floor(t);
    vec2 f = t - base;
    vec4 shares = vec4((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y) * scale;
    ivec2 corner = ivec2(base);
    for (int c = 0; c < 4; c++)
    {
        uint bits = packUnorm4x8(texelFetch(_TerrainControl, ivec3(corner + ivec2(c & 1, c >> 1), int(page.z + 0.5)), 0));
        float blend = float((bits >> 12) & 255u) / 255.0;
        terrain_AddLayer(ids, weights, int(bits & 63u), shares[c] * (1.0 - blend));
        terrain_AddLayer(ids, weights, int((bits >> 6) & 63u), shares[c] * blend);
    }
}

vec2 terrain_Hash(ivec2 p)
{
    uvec2 q = uvec2(p) * uvec2(1597334673u, 3812015801u);
    uint n = (q.x ^ q.y) * 1597334673u;
    return vec2(uvec2(n, n * 3812015801u) >> 8u) / 16777216.0;
}

// Value noise from -1 to 1 with a feature every unit
float Terrain_Noise(vec2 p)
{
    vec2 i = floor(p), f = p - i;
    f = f * f * (3.0 - 2.0 * f);
    ivec2 c = ivec2(i);
    float a = terrain_Hash(c).x, b = terrain_Hash(c + ivec2(1, 0)).x, d = terrain_Hash(c + ivec2(0, 1)).x, e = terrain_Hash(c + ivec2(1, 1)).x;
    return mix(mix(a, b, f.x), mix(d, e, f.x), f.y) * 2.0 - 1.0;
}

// One tap of a hex tile: the texture turned and moved by the tile's own random, its normal turned back
void terrain_HexTap(int layer, vec2 uv, vec2 ddx, vec2 ddy, ivec2 vertex, float weight, inout vec4 albedo, inout vec4 surface)
{
    vec2 random = terrain_Hash(vertex);
    float angle = random.x * 6.2831853;
    float c = cos(angle), s = sin(angle);
    mat2 turn = mat2(c, s, -s, c);
    vec2 centre = vec2(float(vertex.x) + 0.5 * float(vertex.y), float(vertex.y) * 0.8660254) / 3.4641016;
    vec2 at = turn * (uv - centre) + centre + random * 7.0;
    vec4 a = textureGrad(_TerrainAlbedo, vec3(at, float(layer)), turn * ddx, turn * ddy);
    vec4 t = textureGrad(_TerrainSurface, vec3(at, float(layer)), turn * ddx, turn * ddy);
    t.xy = transpose(turn) * (t.xy * 2.0 - 1.0) * 0.5 + 0.5;
    albedo += a * weight;
    surface += t * weight;
}

// One layer seen through one projection: albedo and height, then normal x, y, roughness and occlusion. With hex tiling,
// three taps of randomly turned and moved copies blended across a grid of hexagons, so the texture never visibly repeats
// (Mikkelsen 2022, Practical Real-Time Hex-Tiling)
void terrain_Project(int layer, bool hex, vec2 uv, vec2 ddx, vec2 ddy, out vec4 albedo, out vec4 surface)
{
    if (!hex)
    {
        albedo = textureGrad(_TerrainAlbedo, vec3(uv, float(layer)), ddx, ddy);
        surface = textureGrad(_TerrainSurface, vec3(uv, float(layer)), ddx, ddy);
        return;
    }
    vec2 st = uv * 3.4641016;
    vec2 skewed = vec2(st.x, -0.57735027 * st.x + 1.15470054 * st.y);
    ivec2 base = ivec2(floor(skewed));
    vec3 f = vec3(fract(skewed), 0.0);
    f.z = 1.0 - f.x - f.y;
    float side = step(0.0, -f.z);
    float flip = 2.0 * side - 1.0;
    vec3 w = vec3(-f.z * flip, side - f.y * flip, side - f.x * flip);
    w = pow(max(w, 0.0), vec3(7.0));
    w /= w.x + w.y + w.z;
    int a = int(side);
    albedo = vec4(0.0);
    surface = vec4(0.0);
    terrain_HexTap(layer, uv, ddx, ddy, base + ivec2(a, a), w.x, albedo, surface);
    terrain_HexTap(layer, uv, ddx, ddy, base + ivec2(a, 1 - a), w.y, albedo, surface);
    terrain_HexTap(layer, uv, ddx, ddy, base + ivec2(1 - a, a), w.z, albedo, surface);
}

// The gathered layers at a point in metres from the terrain's corner, its height in y, with how it changes across the
// pixel or texel for filtering, on ground with the normal n
Terrain_Material Terrain_Blend(ivec4 ids, vec4 weights, vec3 local, vec3 dx, vec3 dy, vec3 n)
{
    Terrain_Material m = Terrain_PlainMaterial(n);
    weights /= max(weights.x + weights.y + weights.z + weights.w, 1e-5);
    int strongest = 0;
    for (int k = 0; k < 4; k++)
    {
        if (weights[k] > weights[strongest])
            strongest = k;
    }
    m.strongest = max(ids[strongest], 0);

    vec3 projection = pow(abs(n), vec3(6.0));
    projection = n.y > 0.85 ? vec3(0.0, 1.0, 0.0) : projection / (projection.x + projection.y + projection.z);

    vec4 albedos[4];
    vec4 surfaces[4];
    vec3 normals[4];
    vec4 heights = vec4(0.0);
    vec4 macros = vec4(0.0);
    for (int k = 0; k < 4; k++)
    {
        albedos[k] = vec4(0.0);
        surfaces[k] = vec4(0.0);
        normals[k] = vec3(0.0);
        if (ids[k] < 0 || weights[k] < 0.01)
            continue;
        vec4 data = texelFetch(_TerrainLayerData, ivec2(ids[k], 0), 0);
        bool hex = data.z > 0.5;
        macros[k] = data.w;
        vec4 a, s;
        if (projection.y > 0.0)
        {
            terrain_Project(ids[k], hex, local.xz * data.x, dx.xz * data.x, dy.xz * data.x, a, s);
            vec2 t = s.xy * 2.0 - 1.0;
            albedos[k] += a * projection.y;
            surfaces[k] += s * projection.y;
            normals[k] += vec3(n.x + t.x, n.y, n.z + t.y) * projection.y;
        }
        if (projection.x > 0.0)
        {
            terrain_Project(ids[k], hex, local.zy * data.x, dx.zy * data.x, dy.zy * data.x, a, s);
            vec2 t = s.xy * 2.0 - 1.0;
            albedos[k] += a * projection.x;
            surfaces[k] += s * projection.x;
            normals[k] += vec3(n.x, n.y + t.y, n.z + t.x * sign(n.x)) * projection.x;
        }
        if (projection.z > 0.0)
        {
            terrain_Project(ids[k], hex, local.xy * data.x, dx.xy * data.x, dy.xy * data.x, a, s);
            vec2 t = s.xy * 2.0 - 1.0;
            albedos[k] += a * projection.z;
            surfaces[k] += s * projection.z;
            normals[k] += vec3(n.x + t.x * sign(n.z), n.y + t.y, n.z) * projection.z;
        }
        heights[k] = weights[k] + albedos[k].a * data.y;
    }

    // Height blend: only layers within a narrow band of the highest show
    float top = max(max(heights.x, heights.y), max(heights.z, heights.w));
    vec4 blend = max(heights - (top - 0.1), 0.0) * step(0.01, weights);
    blend /= max(blend.x + blend.y + blend.z + blend.w, 1e-5);

    m.albedo = vec3(0.0);
    vec3 normal = vec3(0.0);
    vec2 rough = vec2(0.0);
    for (int k = 0; k < 4; k++)
    {
        m.albedo += albedos[k].rgb * blend[k];
        normal += normals[k] * blend[k];
        rough += surfaces[k].zw * blend[k];
    }
    // Broad light and dark patches over every layer, so wide areas of one layer are not one flat tone
    float variation = Terrain_Noise(local.xz / 45.0) * 0.6 + Terrain_Noise(local.xz / 11.0) * 0.4;
    m.albedo *= max(1.0 + variation * dot(macros, blend), 0.0);
    m.normal = normalize(dot(normal, normal) > 1e-8 ? normal : n);
    m.roughness = rough.x;
    m.occlusion = rough.y;
    m.weights = blend;
    return m;
}

// The layers' average colours by their weights, kept to a narrow band of the strongest as the height blend does, for
// ground too far for its textures to show
vec3 Terrain_MacroColor(ivec4 ids, vec4 weights)
{
    weights /= max(weights.x + weights.y + weights.z + weights.w, 1e-5);
    float top = max(max(weights.x, weights.y), max(weights.z, weights.w));
    weights = max(weights - (top - 0.15), 0.0);
    weights /= max(weights.x + weights.y + weights.z + weights.w, 1e-5);
    vec3 color = vec3(0.0);
    for (int k = 0; k < 4; k++)
    {
        if (ids[k] >= 0 && weights[k] > 0.0)
            color += textureLod(_TerrainAlbedo, vec3(0.5, 0.5, float(ids[k])), 16.0).rgb * weights[k];
    }
    return color;
}
