#version 450
#define M 1
#define M 2
layout(location=0) out vec4 o;
void main() { o = vec4(float(M)); }
