#version 450
struct S { layout(row_major) mat2x3 m; };
layout(std140, binding=0) uniform U { S s; float f; } u;
layout(location=0) out vec4 c;
void main(){ c = vec4(u.s.m[0], u.f); }
