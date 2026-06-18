#pragma pack_matrix(column_major)
#ifdef SLANG_HLSL_ENABLE_NVAPI
#include "nvHLSLExtns.h"
#endif

#ifndef __DXC_VERSION_MAJOR
// warning X3557: loop doesn't seem to do anything, forcing loop to unroll
#pragma warning(disable : 3557)
#endif


#line 6 "Common.slang"
struct Lighting_0
{
    float4 sunDirection_0;
    float4 sunColor_0;
    float ambientIntensity_0;
};

cbuffer lighting_0 : register(b1)
{
    Lighting_0 lighting_0;
}

#line 15
float3 shade_0(float3 albedo_0, float3 normal_0)
{

    return albedo_0 * (lighting_0.ambientIntensity_0 + max(0.0f, dot(normalize(normal_0), normalize(lighting_0.sunDirection_0.xyz)))) * lighting_0.sunColor_0.xyz;
}


#line 23 "Modules.slang"
struct VertexOutput_0
{
    float4 clipPosition_0 : SV_Position;
    float3 normal_1 : NORMAL;
    float4 color_0 : COLOR;
};


#line 41
float4 fragment(VertexOutput_0 input_0) : SV_TARGET
{
    return float4(shade_0(input_0.color_0.xyz, input_0.normal_1), input_0.color_0.w);
}

