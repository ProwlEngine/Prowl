#pragma pack_matrix(column_major)
#ifdef SLANG_HLSL_ENABLE_NVAPI
#include "nvHLSLExtns.h"
#endif

#ifndef __DXC_VERSION_MAJOR
// warning X3557: loop doesn't seem to do anything, forcing loop to unroll
#pragma warning(disable : 3557)
#endif


#line 13 "Graphics.slang"
struct VertexOutput_0
{
    float4 clipPosition_0 : SV_Position;
    float2 uv_0 : UV;
    float4 color_0 : COLOR;
};


#line 31
float4 fragment(VertexOutput_0 input_0) : SV_TARGET
{
    return input_0.color_0;
}

