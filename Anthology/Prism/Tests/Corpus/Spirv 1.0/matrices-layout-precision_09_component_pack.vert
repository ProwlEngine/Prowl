#version 460
layout(location = 0, component = 0) out float a;
layout(location = 0, component = 1) out float b;
void main() { a = 1.0; b = 2.0; gl_Position = vec4(0.0); }
