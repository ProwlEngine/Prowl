#version 450
layout(location=0) out vec4 o;
void main() { bool b = true; b++; o = vec4(b); }
