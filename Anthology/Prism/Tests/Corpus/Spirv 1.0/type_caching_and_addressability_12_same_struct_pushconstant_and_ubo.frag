#version 450
struct S { float a; vec3 b; };
layout(push_constant) uniform PC { S s; } pc;
layout(std140,set=0,binding=0) uniform U { S s; } u;
layout(location=0) out vec4 o;
void main() { o = vec4(pc.s.a + u.s.a) + vec4(pc.s.b, 1.0); }
