#version 450
layout(std430, binding=0) buffer B { layout(offset=20, align=16) float a; float b; layout(align=8) float c; } b;
layout(location=0) out vec4 o;
void main(){ o = vec4(b.a, b.b, b.c, 1); }
