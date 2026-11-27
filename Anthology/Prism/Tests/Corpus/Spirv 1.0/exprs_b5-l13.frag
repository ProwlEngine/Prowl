#version 450
#extension GL_ARB_gpu_shader_int64 : enable
#extension GL_EXT_shader_explicit_arithmetic_types : enable
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i; layout(location=2) flat in uint u;
const int8_t a = int8_t(127); const int8_t b = a + int8_t(1); const uint8_t c = uint8_t(300); void main(){ o = vec4(b, c, 0, 0); }
