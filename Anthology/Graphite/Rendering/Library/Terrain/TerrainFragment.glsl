// The Terrain surface model's fragment helpers, included before the Surface function: the frame from the normal page
// per pixel blended toward the next level's as the node morphs, so far terrain is lit like near terrain, and the
// node's level and morph for debug views.
#pragma once

#include "Graphite/Terrain/Terrain.glsl"
#include "Graphite/Terrain/TerrainMaterial.glsl"

#ifdef GRAPHITE_FRAGMENT

// Discards the pixel when the quad under it is a hole, the quad's lowest corner holding its bit as the physics reads it
void Terrain_ClipHoles()
{
#ifdef TERRAIN_HOLES
    ivec2 quad = ivec2(floor(vTerrainHole.xy * TERRAIN_PAGE_SIZE - 0.5));
    uint bits = packUnorm4x8(texelFetch(_TerrainControl, ivec3(quad, int(vTerrainHole.z + 0.5)), 0));
    if ((bits & (1u << 20)) != 0u)
        discard;
#endif
}

#endif

#if defined(GRAPHITE_FRAGMENT) && defined(GRAPHITE_STANDARD_FRAME)

void Terrain_Inputs(inout Graphite_SurfaceData i)
{
    vec3 n = normalize(mix(Terrain_DecodeNormal(texture(_TerrainNormals, vTerrainPage)), Terrain_DecodeNormal(texture(_TerrainNormals, vTerrainParent)), vTerrainLod.y));
    vec3 t = normalize(vec3(n.y, -n.x, 0.0));
    i.geometryNormal = n;
    i.tangent = t;
    i.bitangent = cross(n, t);
}

float Terrain_Level() { return vTerrainLod.x; }

float Terrain_Morph() { return vTerrainLod.y; }

#elif defined(GRAPHITE_FRAGMENT)

float Terrain_Level() { return 0.0; }

float Terrain_Morph() { return 0.0; }

#endif
