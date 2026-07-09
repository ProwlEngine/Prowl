#pragma pack_matrix(column_major)
#ifdef SLANG_HLSL_ENABLE_NVAPI
#include "nvHLSLExtns.h"
#endif

#ifndef __DXC_VERSION_MAJOR
// warning X3557: loop doesn't seem to do anything, forcing loop to unroll
#pragma warning(disable : 3557)
#endif


#line 7 "ParameterBlocks.slang"
struct Globals_0
{
    float4x4 viewProj_0;
    float4 tint_0;
};


#line 9
cbuffer globals_0 : register(b0)
{
    Globals_0 globals_0;
}

#line 26
float4 vertex(float3 position_0 : POSITION) : SV_POSITION
{

#line 27
    return mul(globals_0.viewProj_0, float4(position_0, 1.0f)) * globals_0.tint_0;
}

