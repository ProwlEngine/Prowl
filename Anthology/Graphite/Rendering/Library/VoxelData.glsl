// The world's voxel volume, one storage buffer: a header, a colour word per voxel, an occupancy byte per voxel, then
// the occupancy pyramid, a bit per cell of levels 1 to 5, each cell 2^level voxels on a side, then a byte per level 1
// cell of the coarsest level whose cell holding it is empty. Voxels are stored at their world coordinates wrapped by
// the resolution, so the box keeps them in place as it scrolls. Pyramid cells are in those stored coordinates too, so
// each lines up with the world's grid, rebuilt around the regions drawn again.
//
// The occupancy byte holds which of the voxel's 2x2x2 half voxel cells are filled, bit x + 2y + 4z. The colour word
// holds the emissive flag in bit 31, then red, green and blue as 8 bits each, albedo as its square root, emission
// pre-exposed by graphite_Voxel.size.z and stored as e / (1 + e).
//
// GRAPHITE_VOXEL_WRITER declares the buffer as the Voxelize pass writes it, in the view set. GRAPHITE_VOXEL_WRITE
// declares it writable for compute, else it is read only.
#pragma once

struct Graphite_VoxelHeader
{
    uvec4 resolution;   // voxels per axis, then the voxel count
    uvec4 wrap;         // the box's first voxel, wrapped by the resolution
    vec4 size;          // a voxel's edge in metres, its inverse, the emission exposure, unused
    uvec4 levels;       // first word of the occupancy bytes, then of pyramid levels 1 to 3
    uvec4 levels2;      // first word of pyramid levels 4 and 5, every word, first word of the empty levels
};

#if defined(GRAPHITE_VOXEL_WRITER)
layout(set = 1, binding = 47, std430) buffer Graphite_Voxels
#elif defined(GRAPHITE_VOXEL_WRITE)
layout(std430) buffer Graphite_Voxels
#else
layout(std430) readonly buffer Graphite_Voxels
#endif
{
    Graphite_VoxelHeader graphite_Voxel;
    uint graphite_VoxelWords[];
};

#define GRAPHITE_VOXEL_LEVELS 5

uvec3 Graphite_VoxelResolution() { return graphite_Voxel.resolution.xyz; }

// Where a voxel of the box, 0 to the resolution on each axis, is stored
uint Graphite_VoxelIndex(uvec3 voxel)
{
    uvec3 r = graphite_Voxel.resolution.xyz;
    uvec3 s = (voxel + graphite_Voxel.wrap.xyz) % r;
    return s.x + r.x * (s.y + r.y * s.z);
}

// Where a voxel inside the box is stored, as Graphite_VoxelIndex finds it but without dividing
uvec3 Graphite_VoxelStored(uvec3 voxel)
{
    uvec3 r = graphite_Voxel.resolution.xyz, s = voxel + graphite_Voxel.wrap.xyz;
    return s - r * uvec3(greaterThanEqual(s, r));
}

// The octants of a voxel by where it is stored
uint Graphite_VoxelStoredOctants(uvec3 stored)
{
    uvec3 r = graphite_Voxel.resolution.xyz;
    uint i = stored.x + r.x * (stored.y + r.y * stored.z);
    return (graphite_VoxelWords[graphite_Voxel.levels.x + (i >> 2u)] >> ((i & 3u) * 8u)) & 0xFFu;
}

uint Graphite_VoxelColorWord(uvec3 voxel) { return graphite_VoxelWords[Graphite_VoxelIndex(voxel)]; }

uint Graphite_VoxelOctants(uvec3 voxel)
{
    uint i = Graphite_VoxelIndex(voxel);
    return (graphite_VoxelWords[graphite_Voxel.levels.x + (i >> 2u)] >> ((i & 3u) * 8u)) & 0xFFu;
}

uint graphite_VoxelLevelStart(uint level) { return level <= 3u ? graphite_Voxel.levels[level] : graphite_Voxel.levels2[level - 4u]; }

// Whether any voxel of a pyramid cell, at level 1 to 5 in stored coordinates, is filled
bool Graphite_VoxelCellFilled(uint level, uvec3 cell)
{
    uvec3 r = graphite_Voxel.resolution.xyz >> level;
    uint i = cell.x + r.x * (cell.y + r.y * cell.z);
    return (graphite_VoxelWords[graphite_VoxelLevelStart(level) + (i >> 5u)] & (1u << (i & 31u))) != 0u;
}

// The coarsest pyramid level whose cell holding a stored voxel is empty, read from a byte per level 1 cell, 0 where that
// cell is filled
uint Graphite_VoxelEmptyLevel(uvec3 stored)
{
    uvec3 r = graphite_Voxel.resolution.xyz >> 1u, cell = stored >> 1u;
    uint i = cell.x + r.x * (cell.y + r.y * cell.z);
    return (graphite_VoxelWords[graphite_Voxel.levels2.w + (i >> 2u)] >> ((i & 3u) * 8u)) & 0xFFu;
}

bool Graphite_VoxelIsEmissive(uint word) { return (word & 0x80000000u) != 0u; }

vec3 graphite_VoxelChannels(uint word) { return vec3(uvec3(word >> 16u, word >> 8u, word) & 0xFFu) / 255.0; }

vec3 Graphite_VoxelAlbedo(uint word)
{
    vec3 c = graphite_VoxelChannels(word);
    return Graphite_VoxelIsEmissive(word) ? vec3(0.0) : c * c;
}

// Emission in nits pre-exposed by the header's exposure, as the voxels were written
vec3 Graphite_VoxelEmission(uint word)
{
    vec3 t = min(graphite_VoxelChannels(word), vec3(254.0 / 255.0));
    return Graphite_VoxelIsEmissive(word) ? t / (1.0 - t) : vec3(0.0);
}

uint graphite_VoxelPack(vec3 albedo, vec3 emission)
{
    bool emissive = max(emission.r, max(emission.g, emission.b)) > 0.0;
    vec3 e = emission * graphite_Voxel.size.z;
    vec3 c = emissive ? e / (1.0 + e) : sqrt(clamp(albedo, 0.0, 1.0));
    uvec3 q = uvec3(round(clamp(c, 0.0, 1.0) * 255.0));
    return (emissive ? 0x80000000u : 0u) | (q.r << 16u) | (q.g << 8u) | q.b;
}

#if defined(GRAPHITE_VOXEL_WRITER) && defined(GRAPHITE_FRAGMENT)
// Whether the fragment's triangle faces the axis of the layer drawing it most, so every triangle lands in the voxels
// once. It takes derivatives, so call it before anything that can discard.
bool Graphite_VoxelFacesLayer(vec3 relativePos)
{
    vec3 n = abs(cross(dFdx(relativePos), dFdy(relativePos)));
    uint axis = n.x >= n.y && n.x >= n.z ? 0u : n.y >= n.z ? 1u : 2u;
    return axis == uint(GRAPHITE_VIEW_INDEX);
}

// Marks the half voxel cell holding a point relative to the box's centre and keeps the larger colour word, emissive
// over albedo
void Graphite_WriteVoxel(vec3 relativePos, vec3 albedo, vec3 emission)
{
    uvec3 r = graphite_Voxel.resolution.xyz;
    vec3 halves = floor((relativePos * graphite_Voxel.size.y + vec3(r) * 0.5) * 2.0);
    if (any(lessThan(halves, vec3(0.0))) || any(greaterThanEqual(halves, vec3(r * 2u))))
        return;
    uvec3 h = uvec3(halves);
    uint i = Graphite_VoxelIndex(h >> 1u);
    uint octant = 1u << ((h.x & 1u) | ((h.y & 1u) << 1u) | ((h.z & 1u) << 2u));
    atomicOr(graphite_VoxelWords[graphite_Voxel.levels.x + (i >> 2u)], octant << ((i & 3u) * 8u));
    atomicMax(graphite_VoxelWords[i], graphite_VoxelPack(albedo, emission));
}
#endif
