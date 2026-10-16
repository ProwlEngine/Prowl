#version 460
#extension GL_EXT_shader_explicit_arithmetic_types : require
layout(location=0) out vec4 o;
void main(){ uint16_t a = uint16_t(5); uint16_t c = -a; o = vec4(float(c)); }
