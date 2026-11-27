#version 450
layout(location=0) out vec4 o;
#define A(x) x
#define B A(
void main(){ o=vec4(B 1.0)); }