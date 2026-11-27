// Impostor frames, as Prowl.Graphite.Rendering.ImpostorFrames works them out: an N by N grid over an octahedron, y up,
// each axis of the grid -1 to 1. A frame looks along minus its direction, a texel's u along the frame's right and v along
// its up, each from minus the radius at 0 to plus it at 1.
#pragma once

vec3 Graphite_ImpostorDirection(vec2 grid, bool hemi)
{
    if (hemi)
    {
        float px = (grid.x + grid.y) * 0.5, pz = (grid.x - grid.y) * 0.5;
        return normalize(vec3(px, 1.0 - abs(px) - abs(pz), pz));
    }
    vec3 n = vec3(grid, 1.0 - abs(grid.x) - abs(grid.y));
    if (n.z < 0.0)
        n.xy = (1.0 - abs(n.yx)) * vec2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
    return normalize(vec3(n.x, n.z, n.y));
}

// The grid point of a direction, the inverse of Graphite_ImpostorDirection, the lower half folded up for hemi frames
vec2 Graphite_ImpostorGrid(vec3 d, bool hemi)
{
    if (hemi)
    {
        d.y = max(d.y, 0.0);
        d /= abs(d.x) + abs(d.y) + abs(d.z);
        return vec2(d.x + d.z, d.x - d.z);
    }
    vec3 n = vec3(d.x, d.z, d.y);
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    return n.z >= 0.0 ? n.xy : (1.0 - abs(n.yx)) * vec2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
}

void Graphite_ImpostorBasis(vec3 d, out vec3 right, out vec3 up)
{
    vec3 upReference = abs(d.y) > 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
    right = normalize(cross(upReference, -d));
    up = cross(-d, right);
}
