#version 450
layout(location=0) out vec4 o;
#define A(x) x
void main(){ o=vec4(A((1.0,2.0))); }