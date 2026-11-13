// The Terrain surface model's vertex stage. Each draw is one TerrainSurface node, a grid of quads in object space
// scaled to its level's spacing, its heights from its page in the pools. Toward the outer edge of its range a node's odd
// vertices slide onto their even neighbours, so at the edge it is exactly the next level's grid and meets it without
// cracks. Per node: custom0 the slice, the view's material cache slot, -1 for none, and the level, custom1 the morph start in
// metres, one over the morph's length and the node's first sample on its level, color the LOD viewer from the node's
// corner in metres and the next level's slice, -1 at the top. The pixel's normal blends toward the next level's by the
// same morph, so a node turning into its parent changes neither shape nor shading.
#pragma once

#include "Graphite/Terrain/Terrain.glsl"

#ifdef GRAPHITE_VERTEX

float terrain_Sample(int slice, ivec2 texel) { return texelFetch(_TerrainHeights, ivec3(texel, slice), 0).r; }

// The height in metres at a point of a page in samples, bilinear between the four around it
float Terrain_Height(float slice, vec2 at)
{
    vec2 base = floor(at);
    vec2 f = at - base;
    ivec2 t = ivec2(base) + 1;
    int s = int(slice + 0.5);
    float h00 = terrain_Sample(s, t), h10 = terrain_Sample(s, t + ivec2(1, 0));
    float h01 = terrain_Sample(s, t + ivec2(0, 1)), h11 = terrain_Sample(s, t + ivec2(1, 1));
    return mix(mix(h00, h10, f.x), mix(h01, h11, f.x), f.y) * _TerrainParams.y + _TerrainParams.x;
}

vec3 Terrain_Normal(float slice, vec2 at) { return Terrain_DecodeNormal(textureLod(_TerrainNormals, vec3(Terrain_PageUV(at), slice), 0.0)); }

void Terrain_Vertex()
{
    Graphite_ObjectData object = Graphite_Object();
    vec4 node = object.custom0;
    vec4 lod = object.custom1;
    float scale = exp2(node.w);
    float spacing = _TerrainSpacing.x * scale;

    // Grid points past the world's edge are held on it
    vec2 limit = _TerrainParams.zw / scale - lod.zw;
    vec2 grid = min(vertexPosition.xz, limit);
    vec2 offset = mod(lod.zw, 128.0);
    float h = Terrain_Height(node.x, offset + grid);
    float morph = clamp((distance(vec3(grid.x * spacing, h, grid.y * spacing), object.color.xyz) - lod.x) * lod.y, 0.0, 1.0);
    grid = min(grid - fract(grid * 0.5) * 2.0 * morph, limit);
    vec2 page = offset + grid;

    Graphite_SurfaceVertex v;
    v.position = vec3(grid.x, Terrain_Height(node.x, page), grid.y);
    v.normal = Terrain_Normal(node.x, page);
    v.tangent = vec4(1.0, 0.0, 0.0, 1.0);
    v.color = vec4(1.0);
    v.uv = (lod.zw + grid) * scale / _TerrainParams.zw;
    v.uv1 = v.uv;
    v.time = Graphite_View().time.x;
#ifdef GRAPHITE_SURFACE_VERTEX_FUNCTION
    SurfaceVertex(v);
#endif

    vec3 relative = Graphite_ObjectToRelative(v.position);
    gl_Position = Graphite_RelativeToClip(relative);
    Graphite_SetPreviousPosition(v.position);

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_VOXEL) || defined(GRAPHITE_STANDARD_ALPHA) || defined(GRAPHITE_STANDARD_SHADOW_DITHER) || defined(GRAPHITE_STANDARD_PREPASS_NORMALS) || defined(GRAPHITE_STANDARD_IMPOSTOR)
    vUV = v.uv;
    vUV1 = v.uv1;
#endif

#ifdef GRAPHITE_STANDARD_VERTEX_DATA
    vRelativePos = relative;
    vColor = v.color;
#endif

#ifdef TERRAIN_HOLES
    vTerrainHole = vec3(Terrain_PageUV(page), node.x);
#endif

#ifdef GRAPHITE_STANDARD_FRAME
    vNormal = v.normal;
    vTangent = normalize(vec3(v.normal.y, -v.normal.x, 0.0));
    vBitangent = cross(v.normal, vTangent);
    vHasTangents = 1.0;
    vTerrainPage = vec3(Terrain_PageUV(page), node.x);
    vTerrainParent = object.color.w < 0.0 ? vTerrainPage : vec3(Terrain_PageUV(mod(lod.zw * 0.5, 128.0) + grid * 0.5), object.color.w);
    vTerrainLod = vec2(node.w, morph);
    vTerrainSlot = node.y;
    vec2 local = (lod.zw + grid) * spacing;
    vTerrainLocal = vec3(local.x, v.position.y, local.y);
#endif

#ifdef GRAPHITE_STANDARD_LIT
    vLightmapUV = vec2(0.0);
#endif
}

#endif
