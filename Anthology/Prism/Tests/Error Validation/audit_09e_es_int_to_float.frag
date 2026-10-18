#version 310 es
precision highp float;
layout(location=0) out vec4 o;
void main() { int i = 2; float f = i; o = vec4(f); }
