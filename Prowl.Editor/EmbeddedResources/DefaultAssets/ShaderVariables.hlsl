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

// Camera parameters
float4 _WorldSpaceCameraPos;
float4 _ProjectionParams;
float4 _ScreenParams;

// Time parameters
float4 _Time;
float4 _SinTime;
float4 _CosTime;
float4 prowl_DeltaTime;

// Fog parameters
float4 prowl_FogColor;  // RGB color of fog
float4 prowl_FogParams; // Packed parameters:
                        // x: density/sqrt(ln(2)) - for Exp2 fog
                        // y: density/ln(2) - for Exp fog
                        // z: -1/(end-start) - for Linear fog
                        // w: end/(end-start) - for Linear fog
float3 prowl_FogStates; // x: 1 if linear is enabled, 0 otherwise
                        // y: 1 if exp fog, 0 otherwise
                        // z: 1 if exp2 fog, 0 otherwise

// Ambient light parameters
float2 prowl_AmbientMode;    // x: uniform, y: hemisphere
float4 prowl_AmbientColor;
float4 prowl_AmbientSkyColor;
float4 prowl_AmbientGroundColor;

// Light parameters
float2 prowl_ShadowAtlasSize;

#endif
