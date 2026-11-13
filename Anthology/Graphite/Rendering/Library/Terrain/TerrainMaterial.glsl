// The terrain's material in the pixel shader. Terrain_EvaluateMaterial blends the layers here, from the node's page and
// its parent's control by the morph, see Graphite/Terrain/TerrainLayers.glsl. Terrain_CachedMaterial samples the
// material cache of the pixel's view instead: the finest level whose area holds the pixel, blended into the next over
// its outer edge, and past every level the layers' average colours. Near the camera the strongest layer's normal map
// adds detail at four times its tiling, and steep pixels there blend the layers themselves, as the cache only looks
// down from above. Views without a cache slot blend the layers themselves.
#pragma once

#include "Graphite/Terrain/Terrain.glsl"

uniform sampler2DArray _TerrainCacheAlbedo;     // per slot then level: albedo with its square root taken, occlusion
uniform sampler2DArray _TerrainCacheSurface;    // per slot then level: world normal x and z from 0 to 1, roughness, strongest layer over 63
uniform sampler2D _TerrainCacheLevels;          // per level along x and slot along y: its first texel's corner in metres along x and z, its texel size, 1 when sampled
uniform vec4 _TerrainCacheParams;               // levels, texels a side, 1 when the cache is on, the near detail distance in metres

#if defined(GRAPHITE_FRAGMENT) && defined(GRAPHITE_STANDARD_FRAME)

void terrain_Layers(out ivec4 ids, out vec4 weights)
{
    ids = ivec4(-1);
    weights = vec4(0.0);
    Terrain_Gather(vTerrainPage, 1.0 - vTerrainLod.y, ids, weights);
    Terrain_Gather(vTerrainParent, vTerrainLod.y, ids, weights);
}

Terrain_Material Terrain_EvaluateMaterial(vec3 geometryNormal)
{
    if (_TerrainSpacing.y < 0.5)
        return Terrain_PlainMaterial(geometryNormal);
    ivec4 ids;
    vec4 weights;
    terrain_Layers(ids, weights);
    vec3 local = vTerrainLocal;
    return Terrain_Blend(ids, weights, local, dFdx(local), dFdy(local), geometryNormal);
}

// How much a cache level may show at a point: 0 at its area's edge rising to 1 past its blend band, and fading out
// as its texels shrink under a pixel at the point's distance, since the cache has no mips
float terrain_CacheWeight(vec4 level, vec2 xz, float footprint)
{
    vec2 t = (xz - level.xy) / (level.z * _TerrainCacheParams.y);
    float edge = min(min(t.x, 1.0 - t.x), min(t.y, 1.0 - t.y));
    float sharp = clamp(2.0 - footprint / level.z, 0.0, 1.0);
    return level.w > 0.5 ? min(clamp(edge / 0.075, 0.0, 1.0), sharp) : 0.0;
}

void terrain_CacheSample(float slice, vec4 level, vec2 xz, float weight, inout vec4 albedo, inout vec4 surface)
{
    vec3 uv = vec3(xz / (level.z * _TerrainCacheParams.y), slice);
    albedo += texture(_TerrainCacheAlbedo, uv) * weight;
    surface += texture(_TerrainCacheSurface, uv) * weight;
}

// The strongest layer of the texel under a point, read without filtering so neighbouring ids never mix
int terrain_CacheLayer(float slice, vec4 level, vec2 xz)
{
    int side = int(_TerrainCacheParams.y);
    ivec2 texel = ivec2(floor(xz / level.z));
    texel = (texel % side + side) % side;
    return int(texelFetch(_TerrainCacheSurface, ivec3(texel, int(slice)), 0).w * 63.0 + 0.5);
}

// The layers' average colours on the ground's own normal, for pixels past the cache
Terrain_Material terrain_FarMaterial(vec3 geometryNormal)
{
    Terrain_Material m = Terrain_PlainMaterial(geometryNormal);
    if (_TerrainSpacing.y > 0.5)
    {
        ivec4 ids;
        vec4 weights;
        terrain_Layers(ids, weights);
        m.albedo = Terrain_MacroColor(ids, weights);
    }
    return m;
}

Terrain_Material Terrain_CachedMaterial(vec3 geometryNormal)
{
    int slot = int(vTerrainSlot + 0.5);
    if (vTerrainSlot < -0.5)
        return Terrain_EvaluateMaterial(geometryNormal);

    float away = length(vRelativePos);
    float near = _TerrainCacheParams.w;
    float steep = smoothstep(0.65, 0.57, geometryNormal.y) * (1.0 - smoothstep(near * 0.75, near, away));
    if (steep >= 1.0)
        return Terrain_EvaluateMaterial(geometryNormal);

    vec2 xz = vTerrainLocal.xz;
    float footprint = away * 0.001;
    int levels = int(_TerrainCacheParams.x);
    vec4 albedo = vec4(0.0), surface = vec4(0.0);
    float rest = 1.0;
    int detailLayer = -1;
    for (int index = 0; index < levels && rest > 0.0; index++)
    {
        vec4 level = texelFetch(_TerrainCacheLevels, ivec2(index, slot), 0);
        float weight = min(terrain_CacheWeight(level, xz, footprint), rest);
        if (weight <= 0.0)
            continue;
        float slice = float(slot * levels + index);
        terrain_CacheSample(slice, level, xz, weight, albedo, surface);
        if (detailLayer < 0)
            detailLayer = terrain_CacheLayer(slice, level, xz);
        rest -= weight;
    }

    // Whatever weight no level took goes to the far material, so the cache's outer edge fades instead of cutting
    Terrain_Material far = rest > 0.0 ? terrain_FarMaterial(geometryNormal) : Terrain_PlainMaterial(geometryNormal);
    if (rest >= 1.0)
        return far;
    vec2 nxz = surface.xy / (1.0 - rest) * 2.0 - 1.0;
    vec3 cachedNormal = normalize(vec3(nxz.x, sqrt(max(0.0, 1.0 - dot(nxz, nxz))), nxz.y));
    Terrain_Material m = far;
    m.albedo = albedo.rgb / (1.0 - rest);
    m.albedo = mix(m.albedo * m.albedo, far.albedo, rest);
    m.occlusion = mix(albedo.a / (1.0 - rest), 1.0, rest);
    m.normal = normalize(mix(cachedNormal, geometryNormal, rest));
    m.roughness = mix(surface.z / (1.0 - rest), far.roughness, rest);

    float detail = 1.0 - smoothstep(near * 0.5, near, away);
    if (detail > 0.0 && detailLayer >= 0 && _TerrainSpacing.y > 0.5)
    {
        float scale = texelFetch(_TerrainLayerData, ivec2(detailLayer, 0), 0).x * 4.0;
        vec2 uv = xz * scale;
        vec2 t = textureGrad(_TerrainSurface, vec3(uv, float(detailLayer)), dFdx(uv), dFdy(uv)).xy * 2.0 - 1.0;
        m.normal = normalize(m.normal + vec3(t.x, 0.0, t.y) * 0.5 * detail);
    }

    if (steep > 0.0)
    {
        Terrain_Material direct = Terrain_EvaluateMaterial(geometryNormal);
        m.albedo = mix(m.albedo, direct.albedo, steep);
        m.normal = normalize(mix(m.normal, direct.normal, steep));
        m.roughness = mix(m.roughness, direct.roughness, steep);
        m.occlusion = mix(m.occlusion, direct.occlusion, steep);
    }
    return m;
}

#elif defined(GRAPHITE_FRAGMENT)

Terrain_Material Terrain_EvaluateMaterial(vec3 geometryNormal) { return Terrain_PlainMaterial(geometryNormal); }

Terrain_Material Terrain_CachedMaterial(vec3 geometryNormal) { return Terrain_PlainMaterial(geometryNormal); }

#endif
