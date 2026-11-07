// Where a vertex of OceanSurface's rings lands: snapped and folded toward the next ring's grid by its morph, moved by
// the waves, carried by any current, and the ripples, raised to its lake's or river's level (a dry vertex to a wet neighbour's) and dropped by the planet's curve. Every pass drawing the water places its vertices through this, so the
// surface and its underwater mask match. See Graphite/Ocean/Water.shader for the ring data.
#pragma once

#include "Graphite/Vertex.glsl"
#include "Graphite/Ocean/Ocean.glsl"

uniform float _PlanetRadius;

// The water's level at a vertex. A dry one, under the floor, takes the highest level of its wet neighbours a quad
// away, so where a lake meets ground the drop to another level stays under the ground instead of sloping across a quad
float Ocean_VertexLevel(vec2 bed, float spacing)
{
    float level = Ocean_WaterLevel(bed);
    if (Ocean_WaterDepth(bed) >= 0.0)
        return level;
    float wettest = -1e30;
    vec2 steps[4] = vec2[](vec2(spacing, 0.0), vec2(-spacing, 0.0), vec2(0.0, spacing), vec2(0.0, -spacing));
    for (int i = 0; i < 4; i++)
    {
        if (Ocean_WaterDepth(bed + steps[i]) >= 0.0)
            wettest = max(wettest, Ocean_WaterLevel(bed + steps[i]));
    }
    return wettest > -1e29 ? wettest : level;
}

// The camera relative position, the point on the largest wave tile, the point relative to the depth's viewer, where
// the waves move it to, the water's depth there and how far it has morphed toward the next ring, 0 to 1
void Ocean_SurfaceVertex(out vec3 relative, out vec2 wave, out vec2 bed, out vec2 landed, out float depth, out float lodAlpha)
{
    Graphite_ObjectData object = Graphite_Object();
    vec4 ring = object.custom0;
    vec4 tile = object.custom1;
    float size = tile.z, quads = tile.w;
    vec3 p = vertexPosition;

    lodAlpha = max(abs(ring.x + p.x * size), abs(ring.y + p.z * size)) / size - 1.0;
    float blackPoint = object.color.y;
    lodAlpha = clamp((lodAlpha - blackPoint) / (1.0 - 2.0 * blackPoint), 0.0, 1.0);
    lodAlpha = min(lodAlpha + object.color.x, 1.0);

    vec2 m = fract(ring.zw + p.xz * (quads * 0.25)) - 0.5;
    if (abs(m.x) < 0.375)
        p.x += m.x * lodAlpha * 4.0 / quads;
    if (abs(m.y) < 0.375)
        p.z += m.y * lodAlpha * 4.0 / quads;

    vec2 offset = ring.xy + p.xz * size;
    wave = tile.xy + p.xz * size;
    bed = offset + object.color.zw;
    depth = Ocean_WaterDepth(bed);
    float spacing = size / quads * (1.0 + lodAlpha);
    vec4 displacement = Ocean_FlowDisplacement(wave, Ocean_Flow(bed), length(offset), Ocean_CascadeDepths(bed), spacing);
    displacement.xyz += Ocean_RippleDisplacement(bed);
    vec2 moved = offset + displacement.xz;
    landed = bed + displacement.xz;
    float drop = _PlanetRadius > 0.0 ? dot(moved, moved) / (2.0 * _PlanetRadius) : 0.0;

    relative = Graphite_ObjectToRelative(vec3(p.x + displacement.x / size, displacement.y + Ocean_VertexLevel(bed, spacing) - drop, p.z + displacement.z / size));
}
