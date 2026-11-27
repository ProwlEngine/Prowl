#version 460
layout(binding = 0, std140) uniform U { vec3 a; float b; vec3 c; float d; };
layout(location = 0) out vec4 o;
void main() { o = vec4(a + c, b + d); }
