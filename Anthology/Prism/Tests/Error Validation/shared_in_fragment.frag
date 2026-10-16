#version 450
shared float tile[4];
layout(location=0) out vec4 o;
void main() { tile[0] = 1.0; o = vec4(tile[0]); }
