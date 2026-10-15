#version 460
#extension GL_EXT_shader_explicit_arithmetic_types : require
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;

void main(){ u16vec2 a = u16vec2(0x8000); o = vec4(vec2(a << uint16_t(1)), vec2(a + a)); }
