#version 450
layout(std140, binding=0, align=32) uniform U { float a; float b; vec2 c; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.a, u.b, u.c); }
