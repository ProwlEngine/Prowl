#version 450
layout(location=0, std140, binding=0) uniform U { float a; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.a); }
