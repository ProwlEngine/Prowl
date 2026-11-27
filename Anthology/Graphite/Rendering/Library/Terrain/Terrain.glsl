// What the Terrain surface model shares between its stages: the page pools TerrainSurface fills, one page a slice
// with a one sample apron, the material layers, and the varyings that carry a pixel's place in them.
#pragma once

#include "Graphite/Standard/Standard.glsl"
#include "Graphite/Terrain/TerrainLayers.glsl"

uniform sampler2DArray _TerrainHeights;
uniform sampler2DArray _TerrainNormals;
uniform vec4 _TerrainParams;    // the height of a 0 sample, metres to a 1 sample, the finest level's quads along x and z

#ifdef TERRAIN_HOLES
GRAPHITE_STANDARD_VARYING vec3 vTerrainHole;    // uv in the pools and the slice, for the hole bits in every pass
#endif

#ifdef GRAPHITE_STANDARD_FRAME
GRAPHITE_STANDARD_VARYING vec3 vTerrainPage;    // uv in the pools and the slice
GRAPHITE_STANDARD_VARYING vec3 vTerrainParent;  // the same in the next level's page, slice -1 at the top level
GRAPHITE_STANDARD_VARYING vec2 vTerrainLod;     // the node's level and how far the vertex has morphed toward the next
GRAPHITE_STANDARD_VARYING vec3 vTerrainLocal;   // metres from the terrain's corner, its height in y
flat GRAPHITE_STANDARD_VARYING float vTerrainSlot; // the view's material cache slot, -1 for none
#endif

// The pools' uv of a point of a page, in samples from its corner
vec2 Terrain_PageUV(vec2 at) { return (at + 1.5) / TERRAIN_PAGE_SIZE; }

// A normal page texel: x and z from 0 to 1, y rebuilt
vec3 Terrain_DecodeNormal(vec4 texel)
{
    vec2 xz = texel.xy * 2.0 - 1.0;
    return normalize(vec3(xz.x, sqrt(max(0.0, 1.0 - dot(xz, xz))), xz.y));
}
