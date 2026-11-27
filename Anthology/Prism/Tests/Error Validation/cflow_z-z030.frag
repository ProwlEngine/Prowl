#version 450
layout(location=0) out vec4 o;
void main(){ o = vec4(1); vec4 v = v.xyzw; o = v; }