#version 450
layout(location=0) in float a;
layout(location=0) out vec4 o;
void main(){ o = vec4(dot(a, 0.5)); }
