#version 460 core

// =============================================================
//  LightBVH.glsl (inlined, standalone)
//
//  This file only needs its own uniforms to compile and run, so per the conversion spec it
//  skips GlobalUniforms/PerObjectUniforms/LightingUniforms entirely and uses a self-contained
//  uniform scheme instead.
// =============================================================
//
// Each Scene maintains two BVHs (static + dynamic). Per fragment the shader walks both trees
// and accumulates contributions from every light whose AABB contains worldPos. Layout MUST
// stay in lockstep with `LightBVH.cs` and `LightBVHTextures.cs`.
//
// LIGHT DATA TEXTURE (RGBA32F, square, power-of-2 width):
//   slot s, linear texel base = s * 5:
//     +0 : Position.xyz, Range
//     +1 : Color.rgb,    Intensity
//     +2 : Direction.xyz, TypeAndFlags   (low 2 bits: type, bit 2: ShadowEnabled)
//     +3 : SpotCos, InnerSpotCos, ShadowBias, ShadowNormalBias
//          (only fetched when type==Spot OR ShadowEnabled)
//     +4 : ShadowStrength, ShadowQuality, ShadowSlotAsFloat, padding
//          (only fetched when ShadowEnabled)
//
// NODE TEXTURE (RGBA32F, square, power-of-2 width):
//   node n, linear texel base = n * 2:
//     +0 : Min.xyz, Hit  (floatBitsToInt to recover signed int)
//     +1 : Max.xyz, Miss
//
//   Internal: Hit = first child index. Miss = "escape" index (-1 = traversal complete).
//   Leaf:     Hit = -(slot + 1).         Miss = escape.

#ifndef PROWL_LIGHT_BVH
#define PROWL_LIGHT_BVH

layout(std140, set = 0, binding = 0) uniform LightBVHStandaloneUniforms
{
    // Texture size and the matching log2(size) so the shader can compute (x,y) from a linear index
    // with bit-shifts, avoiding a runtime integer division on every fetch (one of the GPU's slowest
    // operations). The mask is implicit as (size - 1) since size is power of 2.
    int _StaticLightTexSize;     // pow-of-2 width = height
    int _StaticLightTexShift;    // log2(_StaticLightTexSize)
    int _StaticNodeTexSize;
    int _StaticNodeTexShift;
    int _DynamicLightTexSize;
    int _DynamicLightTexShift;
    int _DynamicNodeTexSize;
    int _DynamicNodeTexShift;

    int _StaticLightRoot;        // -1 = empty tree, skip traversal
    int _DynamicLightRoot;
};

layout(set = 0, binding = 1) uniform sampler2D _StaticLightData;
layout(set = 0, binding = 2) uniform sampler2D _StaticLightNodes;
layout(set = 0, binding = 3) uniform sampler2D _DynamicLightData;
layout(set = 0, binding = 4) uniform sampler2D _DynamicLightNodes;

// Hard upper bound on the per-fragment traversal loop. Well-formed trees terminate via
// miss == -1 long before this; the cap exists only so a corrupted texture can't infinite-loop
// the fragment shader. 4096 covers a 4096-node tree (~2k lights) end-to-end.
#ifndef LBVH_MAX_NODE_VISITS
#define LBVH_MAX_NODE_VISITS 4096
#endif

struct LightSample
{
    vec3  Position;
    float Range;
    vec3  Color;
    float Intensity;
    vec3  Direction;
    int   Type;            // 0 directional, 1 point, 2 spot
    float SpotCos;         // cos(outer)
    float InnerSpotCos;    // cos(inner)
    float ShadowBias;
    float ShadowNormalBias;
    float ShadowStrength;
    float ShadowQuality;
    int   ShadowSlot;      // -1 if no atlas slot this frame
    int   ShadowEnabled;   // 0 / 1
};

// Wrap a linear texel index into a 2D coordinate. `dim` must be a power of 2 and `shift` must
// equal log2(dim). The shift makes the y-coordinate a bit-shift instead of an integer division
// (`/` against a uniform divisor compiles to a real `idiv` on most GPUs).
ivec2 LBVH_Coord(int linearIdx, int dim, int shift)
{
    int mask = dim - 1;
    return ivec2(linearIdx & mask, linearIdx >> shift);
}

// Lazy load: only fetch the texels each light type / shadow state actually needs. An unshadowed
// point light reads 3 texels; a shadowed spot light reads 5. Particle systems with hundreds of
// unshadowed point lights save ~40 % of light-data bandwidth here.
LightSample LBVH_FetchLight(sampler2D tex, int dim, int shift, int slot)
{
    int base = slot * 5;
    vec4 t0 = texelFetch(tex, LBVH_Coord(base + 0, dim, shift), 0);
    vec4 t1 = texelFetch(tex, LBVH_Coord(base + 1, dim, shift), 0);
    vec4 t2 = texelFetch(tex, LBVH_Coord(base + 2, dim, shift), 0);

    int typeAndFlags = floatBitsToInt(t2.w);
    int type = typeAndFlags & 3;
    int shadowEnabled = (typeAndFlags >> 2) & 1;

    LightSample L;
    L.Position      = t0.xyz;
    L.Range         = t0.w;
    L.Color         = t1.rgb;
    L.Intensity     = t1.w;
    L.Direction     = t2.xyz;
    L.Type          = type;
    L.ShadowEnabled = shadowEnabled;

    // Texel 3 carries spot cosines AND shadow biases. Both spot lighting and shadow sampling
    // need it; everything else (an unshadowed point) skips it entirely.
    if (type == 2 || shadowEnabled != 0)
    {
        vec4 t3 = texelFetch(tex, LBVH_Coord(base + 3, dim, shift), 0);
        L.SpotCos          = t3.x;
        L.InnerSpotCos     = t3.y;
        L.ShadowBias       = t3.z;
        L.ShadowNormalBias = t3.w;
    }
    else
    {
        // Defaults that make the spot-cone smoothstep pass through (1.0) and shadow biases
        // harmless if some downstream code reads them anyway.
        L.SpotCos          = -1.0;
        L.InnerSpotCos     =  1.0;
        L.ShadowBias       =  0.0;
        L.ShadowNormalBias =  0.0;
    }

    if (shadowEnabled != 0)
    {
        vec4 t4 = texelFetch(tex, LBVH_Coord(base + 4, dim, shift), 0);
        L.ShadowStrength = t4.x;
        L.ShadowQuality  = t4.y;
        L.ShadowSlot     = floatBitsToInt(t4.z);
    }
    else
    {
        L.ShadowStrength = 0.0;
        L.ShadowQuality  = 0.0;
        L.ShadowSlot     = -1;
    }
    return L;
}

// Stateless iterator over a rope BVH. Initialize with LBVH_Begin, then call LBVH_Next in a
// loop until it returns -1. Each non-negative return is a slot index whose AABB contains
// `worldPos`; the caller fetches its light data via LBVH_FetchLight.
struct LBVH_Iter
{
    int current;
};

void LBVH_Begin(out LBVH_Iter it, int root)
{
    it.current = root;
}

int LBVH_Next(inout LBVH_Iter it, sampler2D nodeTex, int dim, int shift, vec3 worldPos)
{
    int budget = LBVH_MAX_NODE_VISITS;
    while (it.current >= 0 && budget > 0)
    {
        budget--;
        int base = it.current * 2;
        vec4 lo = texelFetch(nodeTex, LBVH_Coord(base + 0, dim, shift), 0);
        vec4 hi = texelFetch(nodeTex, LBVH_Coord(base + 1, dim, shift), 0);

        int hitNext  = floatBitsToInt(lo.w);
        int missNext = floatBitsToInt(hi.w);

        if (hitNext < 0)
        {
            // Leaf. The leaf's AABB is the light's range cube (pos +- range, axis-aligned), so
            // testing against the inscribed sphere is tighter (~50% smaller hit volume) and
            // strictly cheaper (1 dot vs 6 compares). Saves us from running 5 texelFetches +
            // PBR + shadow sampling for fragments that landed in the AABB corners but are
            // outside the actual sphere of influence.
            vec3 c = (lo.xyz + hi.xyz) * 0.5;
            float r = (hi.x - lo.x) * 0.5; // symmetric, same in y/z
            vec3 d = worldPos - c;
            it.current = missNext;
            if (dot(d, d) <= r * r)
                return -hitNext - 1;
        }
        else
        {
            // Internal node: AABB test (cheaper to compare than to sphere-test against the
            // bounds of an arbitrary union).
            bool inside =
                worldPos.x >= lo.x && worldPos.x <= hi.x &&
                worldPos.y >= lo.y && worldPos.y <= hi.y &&
                worldPos.z >= lo.z && worldPos.z <= hi.z;
            it.current = inside ? hitNext : missNext;
        }
    }
    return -1;
}

#endif

// =============================================================
//  Standalone test entry point
// =============================================================

layout(location = 0) out vec4 FragColor;

void main()
{
    vec3 worldPos = vec3(0.0, 1.0, 0.0);
    vec3 accumColor = vec3(0.0);
    float accumIntensity = 0.0;

    LBVH_Iter staticIt;
    LBVH_Begin(staticIt, _StaticLightRoot);
    int slot;
    int visited = 0;
    while (visited < LBVH_MAX_NODE_VISITS &&
           (slot = LBVH_Next(staticIt, _StaticLightNodes, _StaticNodeTexSize, _StaticNodeTexShift, worldPos)) >= 0)
    {
        LightSample L = LBVH_FetchLight(_StaticLightData, _StaticLightTexSize, _StaticLightTexShift, slot);
        accumColor += L.Color * L.Intensity;
        accumIntensity += L.Intensity;
        visited++;
    }

    LBVH_Iter dynamicIt;
    LBVH_Begin(dynamicIt, _DynamicLightRoot);
    visited = 0;
    while (visited < LBVH_MAX_NODE_VISITS &&
           (slot = LBVH_Next(dynamicIt, _DynamicLightNodes, _DynamicNodeTexSize, _DynamicNodeTexShift, worldPos)) >= 0)
    {
        LightSample L = LBVH_FetchLight(_DynamicLightData, _DynamicLightTexSize, _DynamicLightTexShift, slot);
        accumColor += L.Color * L.Intensity;
        accumIntensity += L.Intensity;
        visited++;
    }

    FragColor = vec4(accumColor, accumIntensity);
}
