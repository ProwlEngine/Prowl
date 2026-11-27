#version 460
layout(location=0) out vec4 o;
layout(location=1) flat in int i;
struct S { vec4 a; float w; };
layout(binding=0) uniform U { S s[3]; } u;
layout(std430, binding=1) buffer W { S keep[3]; } w;
void main(){ S l[3] = u.s; S m2[3] = l; w.keep = l; o = m2[i].a + vec4(l[2].w); }
