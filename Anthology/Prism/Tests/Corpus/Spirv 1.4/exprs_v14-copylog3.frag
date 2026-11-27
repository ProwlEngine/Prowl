#version 460
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
struct S { vec4 a; }; layout(binding=0) uniform U { S s[2]; } u; void main(){ S l[2] = u.s; o = l[i].a; }
