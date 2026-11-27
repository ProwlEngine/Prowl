#version 460
#extension GL_EXT_shader_explicit_arithmetic_types : require
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;

void main(){ uint16_t a = uint16_t(0xFFFF); int16_t x = int16_t(a); o=vec4(float(x)); }
