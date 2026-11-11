// The world's radiance cache: a hash of voxel faces, each holding the light arriving at it, irradiance over pi, so a
// face's albedo times it is the light it sends back from the bounces before. Entries are made where hits ask for one,
// updated a part of the table at a time by tracing a ray from each face, and let go once unread or once their voxel
// empties. A key is the face's voxel at its stored, wrapped coordinates, 9 bits an axis, and the face, plus 1 so 0
// marks an empty slot. A slot is two words: the key, the frames since a hit last read it, then the bounced light as
// halves with its sample count, then the local lights' light there as halves. See Internal/VoxelCacheUpdate.glsl.
#pragma once

#include "Graphite/Math.glsl"
#include "Graphite/VoxelLighting.glsl"

#define GRAPHITE_VOXEL_CACHE_SIZE (1u << 20)
#define GRAPHITE_VOXEL_CACHE_PROBES 8u
#define GRAPHITE_VOXEL_CACHE_NONE 0xFFFFFFFFu

layout(std430) buffer Graphite_VoxelCache { uvec4 graphite_VoxelCacheEntries[]; };   // two a slot

// A face as 0 to 5: the axis times two, plus one facing down it
uint Graphite_VoxelFace(vec3 normal)
{
    vec3 a = abs(normal);
    uint axis = a.x >= a.y && a.x >= a.z ? 0u : a.y >= a.z ? 1u : 2u;
    return axis * 2u + (normal[axis] < 0.0 ? 1u : 0u);
}

vec3 Graphite_VoxelFaceNormal(uint face)
{
    vec3 n = vec3(0.0);
    n[face >> 1u] = (face & 1u) != 0u ? -1.0 : 1.0;
    return n;
}

uint graphite_VoxelCacheKey(uvec3 voxel, uint face)
{
    uvec3 s = (voxel + graphite_Voxel.wrap.xyz) % graphite_Voxel.resolution.xyz;
    return (s.x | (s.y << 9u) | (s.z << 18u) | (face << 27u)) + 1u;
}

// The slot holding a voxel face's light, marked read. When it has none and spawn is set, one is made if a probe slot
// is free. NONE otherwise.
uint Graphite_VoxelCacheSlot(uvec3 voxel, uint face, bool spawn)
{
    uint key = graphite_VoxelCacheKey(voxel, face);
    uint start = Graphite_Hash(key);
    uint empty = GRAPHITE_VOXEL_CACHE_NONE;
    for (uint i = 0u; i < GRAPHITE_VOXEL_CACHE_PROBES; i++)
    {
        uint slot = (start + i) & (GRAPHITE_VOXEL_CACHE_SIZE - 1u);
        uint found = graphite_VoxelCacheEntries[slot * 2u].x;
        if (found == key)
        {
            graphite_VoxelCacheEntries[slot * 2u].y = 0u;
            return slot;
        }
        if (found == 0u && empty == GRAPHITE_VOXEL_CACHE_NONE)
            empty = slot;
    }
    if (!spawn || empty == GRAPHITE_VOXEL_CACHE_NONE)
        return GRAPHITE_VOXEL_CACHE_NONE;
    uint previous = atomicCompSwap(graphite_VoxelCacheEntries[empty * 2u].x, 0u, key);
    return previous == 0u || previous == key ? empty : GRAPHITE_VOXEL_CACHE_NONE;
}

vec3 Graphite_VoxelCacheLight(uvec4 entry) { return vec3(unpackHalf2x16(entry.z), unpackHalf2x16(entry.w).x); }

vec3 Graphite_VoxelCacheLocal(uvec4 local) { return vec3(unpackHalf2x16(local.x), unpackHalf2x16(local.y).x); }

// The slot of the face a hit landed on, as Graphite_VoxelCacheSlot
uint Graphite_VoxelHitSlot(Graphite_VoxelHit hit, bool spawn)
{
    return hit.normal == vec3(0.0) ? GRAPHITE_VOXEL_CACHE_NONE : Graphite_VoxelCacheSlot(hit.voxel, Graphite_VoxelFace(hit.normal), spawn);
}

// The light arriving at the face a hit landed on, bounced and from local lights, irradiance over pi, making its entry
// when it has none and spawn is set
vec3 Graphite_VoxelIndirect(Graphite_VoxelHit hit, bool spawn)
{
    uint slot = Graphite_VoxelHitSlot(hit, spawn);
    return slot == GRAPHITE_VOXEL_CACHE_NONE ? vec3(0.0) : Graphite_VoxelCacheLight(graphite_VoxelCacheEntries[slot * 2u]) + Graphite_VoxelCacheLocal(graphite_VoxelCacheEntries[slot * 2u + 1u]);
}

// The local lights' light arriving at the face a hit landed on, irradiance over pi, as Graphite_VoxelIndirect
vec3 Graphite_VoxelLocalIndirect(Graphite_VoxelHit hit, bool spawn)
{
    uint slot = Graphite_VoxelHitSlot(hit, spawn);
    return slot == GRAPHITE_VOXEL_CACHE_NONE ? vec3(0.0) : Graphite_VoxelCacheLocal(graphite_VoxelCacheEntries[slot * 2u + 1u]);
}

// The light a hit voxel sends back along the ray: its direct light, then its albedo, at most 0.9 so bounces between
// faces fade, times the light the cache holds for its face. Only rays from the view spawn entries, so the cache holds
// the faces the view's light comes from rather than spreading over everything the cache's own rays reach.
vec3 Graphite_VoxelHitLight(Graphite_VoxelHit hit, vec3 origin, vec3 direction, bool spawn)
{
    vec3 light = Graphite_VoxelHitRadiance(hit, origin, direction);
    uint word = Graphite_VoxelColorWord(hit.voxel);
    if (Graphite_VoxelIsEmissive(word))
        return light;
    vec3 albedo = Graphite_VoxelAlbedo(word);
    albedo *= min(1.0, 0.9 / max(max(albedo.r, albedo.g), max(albedo.b, 1e-4)));
    return light + albedo * Graphite_VoxelIndirect(hit, spawn);
}
