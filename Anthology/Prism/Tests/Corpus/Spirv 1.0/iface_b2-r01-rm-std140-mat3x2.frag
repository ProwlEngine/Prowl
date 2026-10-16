#version 450
layout(std140, binding=0) uniform U { layout(row_major) mat3x2 a; float f; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.a[2], u.f, 1); }
