#version 450
struct S { vec2 a; float b; };
layout(location=0) flat in S s;
layout(location=0) out vec4 o;
void main(){ o = vec4(s.a, s.b, 1.0); }
