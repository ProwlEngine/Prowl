#version 450
struct S { vec4 a; };
layout(location=0) in S s;
void main(){ gl_Position = s.a; }
