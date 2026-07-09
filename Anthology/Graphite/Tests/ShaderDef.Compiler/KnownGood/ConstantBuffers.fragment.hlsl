#pragma pack_matrix(column_major)
#ifdef SLANG_HLSL_ENABLE_NVAPI
#include "nvHLSLExtns.h"
#endif

#ifndef __DXC_VERSION_MAJOR
// warning X3557: loop doesn't seem to do anything, forcing loop to unroll
#pragma warning(disable : 3557)
#endif


#line 13 "ConstantBuffers.slang"
struct Material_0
{
    float4 baseColor_0;
    float2 tiling_0;
    int flags_0;
};


cbuffer material_0 : register(b1)
{
    Material_0 material_0;
}

#line 6
struct Camera_0
{
    float4x4 viewProj_0;
    float3 cameraPos_0;
    float time_0;
};


#line 20
cbuffer camera_0 : register(b0)
{
    Camera_0 camera_0;
}

#line 30
float4 fragment(float2 uv_0 : UV0) : SV_TARGET
{
    return material_0.baseColor_0 + float4(uv_0 * material_0.tiling_0, camera_0.cameraPos_0.z, float(material_0.flags_0));
}

