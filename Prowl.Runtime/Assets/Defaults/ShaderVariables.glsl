#ifndef SHADER_SHADERVARIABLES
#define SHADER_SHADERVARIABLES

// Default uniforms
uniform mat4 prowl_ObjectToWorld;
uniform mat4 prowl_WorldToObject;
uniform mat4 prowl_PrevObjectToWorld;
uniform mat4 prowl_PrevViewProj;
uniform int _ObjectID;

uniform mat4 prowl_MatV;
uniform mat4 prowl_MatIV;
uniform mat4 prowl_MatP;
uniform mat4 prowl_MatVP;

#define PROWL_MATRIX_V prowl_MatV
#define PROWL_MATRIX_VP_PREVIOUS prowl_PrevViewProj
#define PROWL_MATRIX_I_V prowl_MatIV
#define PROWL_MATRIX_P prowl_MatP
#define PROWL_MATRIX_VP prowl_MatVP
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

// Camera parameters
uniform vec3 _WorldSpaceCameraPos;
uniform vec4 _ProjectionParams;
uniform vec4 _ScreenParams;
uniform vec2 _CameraJitter;
uniform vec2 _CameraPreviousJitter;

// Time parameters
uniform vec4 _Time;
uniform vec4 _SinTime;
uniform vec4 _CosTime;
uniform vec4 prowl_DeltaTime;

// Fog parameters
uniform vec4 prowl_FogColor;  // RGB color of fog
uniform vec4 prowl_FogParams; // Packed parameters:
                             // x: density/sqrt(ln(2)) - for Exp2 fog
                             // y: density/ln(2) - for Exp fog
                             // z: -1/(end-start) - for Linear fog
                             // w: end/(end-start) - for Linear fog
uniform vec3 prowl_FogStates; // x: 1 if linear is enabled, 0 otherwise
                             // y: 1 if exp fog, 0 otherwise
                             // z: 1 if exp2 fog, 0 otherwise

// Ambient light parameters
uniform vec2 prowl_AmbientMode;    // x: uniform, y: hemisphere
uniform vec4 prowl_AmbientColor;
uniform vec4 prowl_AmbientSkyColor;
uniform vec4 prowl_AmbientGroundColor;

// Light parameters
uniform vec2 prowl_ShadowAtlasSize;

#endif