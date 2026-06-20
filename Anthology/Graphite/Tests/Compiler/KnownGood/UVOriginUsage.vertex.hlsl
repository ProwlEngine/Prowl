#pragma pack_matrix(column_major)
#ifdef SLANG_HLSL_ENABLE_NVAPI
#include "nvHLSLExtns.h"
#endif

#ifndef __DXC_VERSION_MAJOR
// warning X3557: loop doesn't seem to do anything, forcing loop to unroll
#pragma warning(disable : 3557)
#endif


#line 14 "UVOriginUsage.slang"
struct VertexOutput_0
{
    float4 clipPosition_0 : SV_Position;
    float2 uv_0 : TEXCOORD0;
};


#line 8
struct VertexInput_0
{
    float3 position_0 : POSITION;
    float2 uv_1 : TEXCOORD0;
};


#line 21
VertexOutput_0 vertex(VertexInput_0 input_0)
{
    VertexOutput_0 output_0;
    output_0.clipPosition_0 = float4(input_0.position_0, 1.0f);
    output_0.uv_0 = input_0.uv_1;
    return output_0;
}

