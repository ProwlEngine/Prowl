#version 450
layout(location=0) in vec4 a[2];
layout(location=0) out vec4 o;
void main() { o = a[0]; gl_Position = a[1]; }
