#pragma pack_matrix(column_major)
#ifdef SLANG_HLSL_ENABLE_NVAPI
#include "nvHLSLExtns.h"
#endif

#ifndef __DXC_VERSION_MAJOR
// warning X3557: loop doesn't seem to do anything, forcing loop to unroll
#pragma warning(disable : 3557)
#endif


#line 10 "ParameterBlocks.slang"
Texture2D<float4 > albedo_0 : register(t0);


#line 11
SamplerState samp_0 : register(s0);

struct PerObject_0
{
    float4 color_0;
    float2 uvOffset_0;
};


#line 18
cbuffer perObject_0 : register(b1)
{
    PerObject_0 perObject_0;
}

#line 18
Texture2D<float4 > perObject_detail_0 : register(t1);


#line 18
SamplerState perObject_detailSamp_0 : register(s1);


#line 18
Texture2D<float4 > onlyTex_tex_0 : register(t2);


#line 18
SamplerState onlyTex_s_0 : register(s2);


#line 30
float4 fragment(float2 uv_0 : UV0) : SV_TARGET
{
    return albedo_0.Sample(samp_0, uv_0) * perObject_0.color_0 + perObject_detail_0.Sample(perObject_detailSamp_0, uv_0 + perObject_0.uvOffset_0) + onlyTex_tex_0.Sample(onlyTex_s_0, uv_0);
}

