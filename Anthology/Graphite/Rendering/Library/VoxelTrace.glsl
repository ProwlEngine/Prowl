// Ray traversal of the world's voxel volume, in voxel units from the box's first corner. Empty space is skipped a
// pyramid cell at a time, the coarsest empty one holding the ray, and filled voxels are tested by their half voxel
// cells, so hits land on the occupancy at twice the colour's resolution. See Graphite/VoxelData.glsl.
#pragma once

#include "Graphite/VoxelData.glsl"

struct Graphite_VoxelHit
{
    bool hit;
    float t;        // along the ray, in voxels
    uvec3 voxel;
    vec3 normal;    // of the face the ray crossed into the hit cell
    uint steps;
    bool lost;      // ran out of steps inside the box, so neither hit nor left it
};

// Where the ray leaves a cell, and across which axis
float graphite_VoxelCellExit(vec3 origin, vec3 inverse, vec3 low, float size, out uint axis)
{
    vec3 t = (low + step(vec3(0.0), inverse) * size - origin) * inverse;
    axis = t.x <= t.y && t.x <= t.z ? 0u : t.y <= t.z ? 1u : 2u;
    return t[axis];
}

vec3 graphite_VoxelFace(uint axis, vec3 direction)
{
    vec3 n = vec3(0.0);
    n[axis] = direction[axis] > 0.0 ? -1.0 : 1.0;
    return n;
}

// The first filled half voxel cell along a ray from origin in direction, both in voxel units from the box's first
// corner, within maxT voxels and maxSteps cells
Graphite_VoxelHit Graphite_TraceVoxels(vec3 origin, vec3 direction, float maxT, uint maxSteps)
{
    const float nudge = 1e-4;
    Graphite_VoxelHit result;
    result.hit = false;
    result.t = maxT;
    result.voxel = uvec3(0u);
    result.normal = vec3(0.0);
    result.steps = 0u;
    result.lost = false;

    vec3 resolution = vec3(Graphite_VoxelResolution());
    vec3 d = mix(direction, vec3(1e-8), equal(direction, vec3(0.0)));
    vec3 inverse = 1.0 / d;
    vec3 near = min(-origin * inverse, (resolution - origin) * inverse);
    vec3 far = max(-origin * inverse, (resolution - origin) * inverse);
    float t = max(max(near.x, near.y), max(near.z, 0.0));
    float leave = min(min(min(far.x, far.y), far.z), maxT);
    uint axis = near.x >= near.y && near.x >= near.z ? 0u : near.y >= near.z ? 1u : 2u;
    vec3 normal = t > 0.0 ? graphite_VoxelFace(axis, d) : vec3(0.0);

    for (uint i = 0u; i < maxSteps && t < leave; i++)
    {
        result.steps = i + 1u;
        vec3 p = origin + d * (t + nudge);
        uvec3 voxel = uvec3(clamp(floor(p), vec3(0.0), resolution - 1.0));
        uvec3 stored = Graphite_VoxelStored(voxel);

        uint level = Graphite_VoxelEmptyLevel(stored);
        uint octants = level == 0u ? Graphite_VoxelStoredOctants(stored) : 0u;
        if (octants != 0u)
        {
            uint voxelAxis;
            float voxelExit = graphite_VoxelCellExit(origin, inverse, vec3(voxel), 1.0, voxelAxis);
            float inner = t;
            for (uint cellStep = 0u; cellStep < 4u && inner < voxelExit; cellStep++)
            {
                vec3 q = origin + d * (inner + nudge);
                vec3 cell = clamp(floor(q * 2.0) - vec3(voxel * 2u), vec3(0.0), vec3(1.0));
                uint bit = uint(cell.x) + 2u * uint(cell.y) + 4u * uint(cell.z);
                if ((octants & (1u << bit)) != 0u)
                {
                    result.hit = true;
                    result.t = inner;
                    result.voxel = voxel;
                    result.normal = normal;
                    return result;
                }
                inner = graphite_VoxelCellExit(origin, inverse, vec3(voxel) + cell * 0.5, 0.5, axis);
                normal = graphite_VoxelFace(axis, d);
            }
            t = voxelExit;
            normal = graphite_VoxelFace(voxelAxis, d);
            continue;
        }

        // The coarsest empty cell holding the voxel, cells lined up with the stored grid, its corner back in the box's
        // coordinates. One straddling the box's edge is empty on both sides, so skipping its inside is still right.
        vec3 low = vec3(voxel) - vec3(stored - ((stored >> level) << level));
        t = graphite_VoxelCellExit(origin, inverse, low, float(1u << level), axis);
        normal = graphite_VoxelFace(axis, d);
    }
    if (t < leave)
    {
        result.lost = true;
        result.t = t;
    }
    return result;
}
