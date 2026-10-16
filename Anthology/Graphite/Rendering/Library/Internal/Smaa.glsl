#pragma variant SMAA BLEND EDGES WEIGHTS
// SMAA 1x with luma edges at the reference's high quality, as Prowl runs it: edge detection, blend weights from the
// area and search tables, then neighbourhood blending. Each pass computes its vertex offsets in the fragment stage,
// as they follow from the texture coordinate alone. Textures are arrays on layered views, the tables too.

uniform vec2 _Resolution;
uniform float _EdgeThreshold;

#define SMAA_RT_METRICS vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y)
#define SMAA_THRESHOLD _EdgeThreshold
#define SMAA_MAX_SEARCH_STEPS 16
#define SMAA_MAX_SEARCH_STEPS_DIAG 8
#define SMAA_CORNER_ROUNDING 25
#define SMAA_INCLUDE_VS 1
#define SMAA_INCLUDE_PS 1

#define SMAA_CUSTOM_SL 1
#ifdef GRAPHITE_LAYERED
#define SMAA_COORD(c) vec3(c, float(gl_ViewIndex))
#else
#define SMAA_COORD(c) c
#endif
#define SMAATexture2D(tex) GRAPHITE_SCREEN_TEXTURE tex
#define SMAATexturePass2D(tex) tex
#define SMAASampleLevelZero(tex, coord) textureLod(tex, SMAA_COORD(coord), 0.0)
#define SMAASampleLevelZeroPoint(tex, coord) textureLod(tex, SMAA_COORD(coord), 0.0)
#define SMAASampleLevelZeroOffset(tex, coord, offset) textureLodOffset(tex, SMAA_COORD(coord), 0.0, offset)
#define SMAASample(tex, coord) texture(tex, SMAA_COORD(coord))
#define SMAASamplePoint(tex, coord) texture(tex, SMAA_COORD(coord))
#define SMAASampleOffset(tex, coord, offset) texture(tex, SMAA_COORD(coord), offset)
#define SMAAGather(tex, coord) textureGather(tex, SMAA_COORD(coord))
#define SMAA_FLATTEN
#define SMAA_BRANCH
#define lerp(a, b, t) mix(a, b, t)
#define saturate(a) clamp(a, 0.0, 1.0)
#define mad(a, b, c) fma(a, b, c)
#define float2 vec2
#define float3 vec3
#define float4 vec4
#define int2 ivec2
#define int3 ivec3
#define int4 ivec4
#define bool2 bvec2
#define bool3 bvec3
#define bool4 bvec4

#include "Graphite/Internal/SmaaReference.glsl"

#if defined(SMAA_EDGES)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;

void main()
{
    vec4 offset[3];
    SMAAEdgeDetectionVS(vUV, offset);
    outColor = vec4(SMAALumaEdgeDetectionPS(vUV, offset, _MainTex), 0.0, 0.0);
}
#elif defined(SMAA_WEIGHTS)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
uniform GRAPHITE_SCREEN_TEXTURE _AreaTex;
uniform GRAPHITE_SCREEN_TEXTURE _SearchTex;

void main()
{
    vec2 pixcoord;
    vec4 offset[3];
    SMAABlendingWeightCalculationVS(vUV, pixcoord, offset);
    outColor = SMAABlendingWeightCalculationPS(vUV, pixcoord, offset, _MainTex, _AreaTex, _SearchTex, vec4(0.0));
}
#else
uniform GRAPHITE_SCREEN_TEXTURE _BlendTex;

void main()
{
    vec4 offset;
    SMAANeighborhoodBlendingVS(vUV, offset);
    outColor = SMAANeighborhoodBlendingPS(vUV, offset, Graphite_Source, _BlendTex);
}
#endif
