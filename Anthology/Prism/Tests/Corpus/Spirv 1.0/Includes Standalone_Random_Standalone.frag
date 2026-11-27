#version 460 core

// =============================================================
// ShaderVariables.glsl (partial) - inlined for _ScreenParams,
// used by the randState global initializer below.
// =============================================================

layout(std140, set = 0, binding = 0) uniform GlobalUniforms { mat4 prowl_MatV; mat4 prowl_MatIV; mat4 prowl_MatP; mat4 prowl_MatVP; mat4 prowl_PrevViewProj; mat4 prowl_MatIP; mat4 prowl_MatIVP; mat4 prowl_MatVP_NonJittered; vec3 _WorldSpaceCameraPos; float _padding0; vec4 _ProjectionParams; vec4 _ScreenParams; vec2 _CameraJitter; vec2 _CameraPreviousJitter; vec4 _Time; vec4 _SinTime; vec4 _CosTime; vec4 prowl_DeltaTime; };

// =============================================================
// Random.glsl - inlined, standalone.
// =============================================================

#ifndef SHADER_RANDOM
#define SHADER_RANDOM

uint triple32(uint x) {
	// https://nullprogram.com/blog/2018/07/31/
	x ^= x >> 17;
	x *= 0xed5ad4bbu;
	x ^= x >> 11;
	x *= 0xac4c1b51u;
	x ^= x >> 15;
	x *= 0x31848babu;
	x ^= x >> 14;
	return uint(x);
}

uint randState = triple32(uint(gl_FragCoord.x + int(_ScreenParams.x) * gl_FragCoord.y) + uint(_ScreenParams.x * _ScreenParams.y) * uint(_ScreenParams.w));
uint RandNext() { return randState = triple32(randState); }
uvec2 RandNext2() { return uvec2(RandNext(), RandNext()); }
uvec3 RandNext3() { return uvec3(RandNext2(), RandNext()); }
uvec4 RandNext4() { return uvec4(RandNext3(), RandNext()); }
float RandNextF() { return float(RandNext()) / float(0xffffffffu); }
vec2 RandNext2F() { return vec2(RandNext2()) / float(0xffffffffu); }
vec3 RandNext3F() { return vec3(RandNext3()) / float(0xffffffffu); }
vec4 RandNext4F() { return vec4(RandNext4()) / float(0xffffffffu); }

float RandF (uint  seed) { return float(triple32(seed))                    / float(0xffffffffu); }
vec2  Rand2F(uvec2 seed) { return vec2(triple32(seed.x), triple32(seed.y)) / float(0xffffffffu); }

#endif

layout(location = 0) out vec4 FragColor;

void main()
{
    float r1 = RandNextF();

    vec2 seedVec = vec2(12.3, 45.6);
    uint seed = uint(seedVec.x * 1000.0) ^ uint(seedVec.y * 1000.0);
    float r2 = RandF(seed);

    float r3 = RandNextF();

    FragColor = vec4(r1, r2, r3, 1.0);
}
