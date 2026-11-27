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
    if (Terrain_IsHole(vTerrainHole))
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

uniform vec4 _TerrainBrush;         // the centre's x and z in metres from the terrain's corner, the radius, the soft share of the radius
uniform vec4 _TerrainBrushColor;    // the tint and how strongly the inside takes it, 0 to hide the brush

// Tints the brush's circle, softly inside and solidly along its rim
void Terrain_Brush(inout PBR_Params p, Graphite_SurfaceData i)
{
    if (_TerrainBrushColor.a <= 0.0 || _TerrainBrush.z <= 0.0)
        return;
    float d = length(vTerrainLocal.xz - _TerrainBrush.xy) / _TerrainBrush.z;
    if (d >= 1.0)
        return;
    float inside = 1.0 - smoothstep(1.0 - _TerrainBrush.w, 1.0, d);
    float rim = smoothstep(0.93, 0.97, d);
    p.baseColor = mix(p.baseColor, _TerrainBrushColor.rgb, max(inside * _TerrainBrushColor.a, rim));
}

float Terrain_Level() { return vTerrainLod.x; }

float Terrain_Morph() { return vTerrainLod.y; }

#elif defined(GRAPHITE_FRAGMENT)

float Terrain_Level() { return 0.0; }

float Terrain_Morph() { return 0.0; }

#endif
