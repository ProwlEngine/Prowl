#version 450
layout(set=0,binding=0) uniform sampler2D a;
layout(set=0,binding=1) uniform sampler2D b;
layout(location=0) out vec4 o;
void main() { o = vec4(a == b); }
