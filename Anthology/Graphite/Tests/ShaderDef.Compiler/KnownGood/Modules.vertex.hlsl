#pragma pack_matrix(column_major)
#ifdef SLANG_HLSL_ENABLE_NVAPI
#include "nvHLSLExtns.h"
#endif

#ifndef __DXC_VERSION_MAJOR
// warning X3557: loop doesn't seem to do anything, forcing loop to unroll
#pragma warning(disable : 3557)
#endif


#line 8 "Modules.slang"
struct Globals_0
{
    float4x4 viewProj_0;
    float4 tint_0;
};

cbuffer globals_0 : register(b0)
{
    Globals_0 globals_0;
}

#line 23
struct VertexOutput_0
{
    float4 clipPosition_0 : SV_Position;
    float3 normal_0 : NORMAL;
    float4 color_0 : COLOR;
};


#line 16
struct VertexInput_0
{
    float3 position_0 : POSITION;
    float3 normal_1 : NORMAL;
    float4 color_1 : COLOR;
};


#line 31
VertexOutput_0 vertex(VertexInput_0 input_0)
{
    VertexOutput_0 output_0;
    output_0.clipPosition_0 = mul(globals_0.viewProj_0, float4(input_0.position_0, 1.0f));
    output_0.normal_0 = input_0.normal_1;
    output_0.color_0 = input_0.color_1 * globals_0.tint_0;
    return output_0;
}

