#version 450
layout(std140) uniform U { vec4 v; } u;
layout(location=0) out vec4 o;
void main() { o = u.v; }
