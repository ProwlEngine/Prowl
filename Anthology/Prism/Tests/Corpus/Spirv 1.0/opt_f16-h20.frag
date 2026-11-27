#version 460
#extension GL_EXT_shader_explicit_arithmetic_types : require
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;

void main(){ float16_t a = float16_t(0.5); o = vec4(float(clamp(a, float16_t(0), float16_t(1))), float(max(a, float16_t(0.25))), float(min(float16_t(-1.0), a)), 1); }
