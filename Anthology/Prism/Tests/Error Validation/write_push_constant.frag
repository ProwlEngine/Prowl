#version 450
layout(push_constant) uniform PC { float a; } pc;
layout(location=0) out vec4 o;
void main() { pc.a = 1.0; o = vec4(pc.a); }
