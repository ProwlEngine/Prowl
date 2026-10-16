#version 450
layout(std140,set=0,binding=0) uniform U { float a; } u;
layout(location=0) out vec4 o;
void main() { u.a = 1.0; o = vec4(u.a); }
