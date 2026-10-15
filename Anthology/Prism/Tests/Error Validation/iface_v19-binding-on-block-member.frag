#version 450
layout(std140, binding=0) uniform U { layout(binding=2) float a; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.a); }
