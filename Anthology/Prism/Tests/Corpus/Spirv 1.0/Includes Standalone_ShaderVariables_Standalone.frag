#version 460 core

// =============================================================
//  ShaderVariables.glsl (inlined, standalone)
// =============================================================

// Global Uniform Buffer containing per-frame shared rendering data
// Uses std140 layout for compatibility across all GPUs
// This data is uploaded once per frame and is constant across all draw calls
layout(std140, set = 0, binding = 0) uniform GlobalUniforms
{
    // Camera matrices
    mat4 prowl_MatV;
    mat4 prowl_MatIV;
    mat4 prowl_MatP;
    mat4 prowl_MatVP;
    mat4 prowl_PrevViewProj;
    mat4 prowl_MatIP;
    mat4 prowl_MatIVP;
    mat4 prowl_MatVP_NonJittered;

    // Camera parameters
    vec3 _WorldSpaceCameraPos;
    float _padding0;

    vec4 _ProjectionParams;
    vec4 _ScreenParams;
    vec2 _CameraJitter;
    vec2 _CameraPreviousJitter;

    // Time parameters
    vec4 _Time;
    vec4 _SinTime;
    vec4 _CosTime;
    vec4 prowl_DeltaTime;
};

#define PROWL_MATRIX_V prowl_MatV
#define PROWL_MATRIX_VP_PREVIOUS prowl_PrevViewProj
#define PROWL_MATRIX_I_V prowl_MatIV
#define PROWL_MATRIX_P prowl_MatP
#define PROWL_MATRIX_VP prowl_MatVP
#define PROWL_MATRIX_I_P prowl_MatIP
#define PROWL_MATRIX_I_VP prowl_MatIVP
#define PROWL_MATRIX_VP_NONJITTERED prowl_MatVP_NonJittered

// Per-object uniforms (set per draw call)
layout(std140, set = 0, binding = 1) uniform PerObjectUniforms
{
    mat4 prowl_ObjectToWorld;
    mat4 prowl_WorldToObject;
    mat4 prowl_PrevObjectToWorld;
    int _ObjectID;
};

#define PROWL_MATRIX_M prowl_ObjectToWorld
#define PROWL_MATRIX_M_PREVIOUS prowl_PrevObjectToWorld

// Derived matrices
mat4 prowl_MatMV = prowl_MatV * prowl_ObjectToWorld;
mat4 prowl_MatMVP = prowl_MatVP * prowl_ObjectToWorld;
mat4 prowl_MatTMV = transpose(prowl_MatV * prowl_ObjectToWorld);
mat4 prowl_MatITMV = transpose(prowl_WorldToObject * prowl_MatIV);

#define PROWL_MATRIX_MV prowl_MatMV
#define PROWL_MATRIX_MVP prowl_MatMVP
#define PROWL_MATRIX_T_MV prowl_MatTMV
#define PROWL_MATRIX_IT_MV prowl_MatITMV

// =============================================================
//  Standalone test entry point
// =============================================================

layout(location = 0) out vec4 FragColor;

void main()
{
    vec4 mvOrigin = prowl_MatMV * vec4(0.0, 0.0, 0.0, 1.0);
    vec4 mvpOrigin = prowl_MatMVP * vec4(0.0, 0.0, 0.0, 1.0);

    FragColor = vec4(_WorldSpaceCameraPos + mvOrigin.xyz + mvpOrigin.xyz, _Time.x);
}
