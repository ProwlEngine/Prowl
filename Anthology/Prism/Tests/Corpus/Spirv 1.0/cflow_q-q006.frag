#version 450
layout(location=0) out vec4 o;
#define L __LINE__
void main(){ o = vec4(L,
L, 0, 0); }
