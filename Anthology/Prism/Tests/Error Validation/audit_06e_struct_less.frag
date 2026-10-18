#version 450
struct S { float a; };
layout(location=0) out vec4 o;
void main() { S p = S(1.0); bool b = p < p; o = vec4(b); }
