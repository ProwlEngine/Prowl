#version 450
layout(location=0) out vec4 o;
void main() { mat2 m = mat2(1.0); bool b = m < m; o = vec4(b); }
