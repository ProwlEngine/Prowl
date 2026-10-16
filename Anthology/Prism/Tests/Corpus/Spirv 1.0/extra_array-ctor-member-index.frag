#version 450
layout(location=0) flat in int i;
layout(location=0) out vec2 o;
struct S { vec2 a[2]; };
void main(){ o = S(vec2[2](vec2(1.0), vec2(2.0))).a[i] + vec2[2](vec2(3.0), vec2(4.0))[i].yx; }
