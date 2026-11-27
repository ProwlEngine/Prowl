#version 450
layout(location=0) out vec4 o;
#define A B
#define B(x) x+1.0
void main(){ o = vec4(A(1.0)); }
