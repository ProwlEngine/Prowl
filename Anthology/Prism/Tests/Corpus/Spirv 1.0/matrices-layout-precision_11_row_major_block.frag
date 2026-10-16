#version 460
layout(binding = 0, std140, row_major) uniform U { mat4 m; };
layout(location = 0) in vec4 v;
layout(location = 0) out vec4 o;
void main() { o = m * v; }
