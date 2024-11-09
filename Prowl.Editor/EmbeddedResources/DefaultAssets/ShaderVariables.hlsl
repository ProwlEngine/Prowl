#ifndef SHADER_SHADERVARIABLES
#define SHADER_SHADERVARIABLES

// Default uniforms buffer
cbuffer _PerDraw
{
    float4x4 prowl_ObjectToWorld;
    float4x4 prowl_WorldToObject;
    float4x4 prowl_PrevObjectToWorld;
    float4x4 prowl_PrevViewProj;

    int _ObjectID;
}

float4x4 prowl_MatV;
float4x4 prowl_MatIV;
float4x4 prowl_MatP;
float4x4 prowl_MatVP;

#define PROWL_MATRIX_V prowl_MatV
#define PROWL_MATRIX_V_PREVIOUS prowl_PrevViewProj
#define PROWL_MATRIX_I_V prowl_MatIV
#define PROWL_MATRIX_P prowl_MatP
#define PROWL_MATRIX_VP prowl_MatVP
#define PROWL_MATRIX_M prowl_ObjectToWorld
#define PROWL_MATRIX_M_PREVIOUS prowl_PrevObjectToWorld

static float4x4 prowl_MatMV = mul(prowl_MatV, prowl_ObjectToWorld);
static float4x4 prowl_MatMVP = mul(prowl_MatVP, prowl_ObjectToWorld);
static float4x4 prowl_MatTMV = transpose(mul(prowl_MatV, prowl_ObjectToWorld));
static float4x4 prowl_MatITMV = transpose(mul(prowl_WorldToObject, prowl_MatIV));
#define PROWL_MATRIX_MV prowl_MatMV
#define PROWL_MATRIX_MVP prowl_MatMVP
#define PROWL_MATRIX_T_MV prowl_MatTMV
#define PROWL_MATRIX_IT_MV prowl_MatITMV


float4 _WorldSpaceCameraPos;
float4 _ProjectionParams;
float4 _ScreenParams;

float4 _Time;
float4 _SinTime;
float4 _CosTime;
float4 prowl_DeltaTime;
float2 prowl_ShadowAtlasSize;

#endif
