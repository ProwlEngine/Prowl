#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
struct S { vec2 a; }; void main(){ S s = S(v.xyz); o = vec4(s.a, 0, 0); }
