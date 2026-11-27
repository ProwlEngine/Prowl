#version 450
layout(row_major) uniform;
layout(std140, binding=0) uniform U { mat2x3 m; float f; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.m[1], u.f); }
