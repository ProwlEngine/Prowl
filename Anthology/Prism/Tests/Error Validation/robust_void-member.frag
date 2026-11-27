#version 450
layout(location=0) in float f;
layout(location=0) out vec4 o;
layout(std430, binding=0) buffer U { void v; float x; } u;
void main(){ o = vec4(u.x); }
