#version 460
layout(location = 0) in vec2 a;
layout(location = 1) in vec2 b;
void main() { mat2 m = mat2(a, b); gl_Position = vec4(m * a, b); }
