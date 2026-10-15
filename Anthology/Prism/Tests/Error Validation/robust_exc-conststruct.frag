#version 450
layout(location=0) in float f;
layout(location=1) in vec4 va;
layout(location=0) out vec4 o;
struct S { float a; vec2 b; int c; }; const S s = S(0.5, vec2(1.0, 2.0));
void main(){ o = vec4(s.c); }
