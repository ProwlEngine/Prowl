#version 330
layout(location=0) out vec4 o;
void main() { int i = 2; uint u = i; o = vec4(float(u)); }
