#version 450
struct S { vec4 a; };
layout(location=0) out S o;
void main(){ o.a = vec4(0); }
