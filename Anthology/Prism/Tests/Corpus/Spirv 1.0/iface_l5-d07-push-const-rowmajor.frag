#version 450
layout(push_constant, row_major) uniform PC { mat2x3 m; float f; } pc;
layout(location=0) out vec4 o;
void main(){ o = vec4(pc.m[1], pc.f); }
