#version 450
layout(std140, binding=0) uniform U { layout(row_major) mat2x4 m[2]; float f; } u;
layout(location=0) out vec4 o;
void main(){ o = u.m[1][1] + u.f; }
