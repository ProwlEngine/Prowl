#version 450
layout(location=0) out vec4 o;
struct S { float a; };
void main(){ S x, y; bool b = x == y; o = vec4(b ? 1.0 : 0.0); }