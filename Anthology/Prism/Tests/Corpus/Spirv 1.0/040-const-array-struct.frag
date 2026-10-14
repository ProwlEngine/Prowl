#version 450
layout(location=0) out vec4 o;
struct S { float a; vec2 b; int c[3]; };
const S s = S(1.0, vec2(2.0), int[3](4,5,6));
const float arr[4] = float[4](0.,1.,2.,3.);
void main(){ o = vec4(s.a, s.b.x, s.c[2], arr[3]); }