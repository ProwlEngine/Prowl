#pragma pack_matrix(column_major)
#ifdef SLANG_HLSL_ENABLE_NVAPI
#include "nvHLSLExtns.h"
#endif

#ifndef __DXC_VERSION_MAJOR
// warning X3557: loop doesn't seem to do anything, forcing loop to unroll
#pragma warning(disable : 3557)
#endif


#line 6 "ConstantBuffers.slang"
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
float4 vertex(float3 position_0 : POSITION) : SV_POSITION
{
    return mul(camera_0.viewProj_0, float4(position_0, 1.0f)) + camera_0.time_0;
}

