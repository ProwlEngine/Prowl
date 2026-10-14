#version 460
layout(location = 0) out vec4 o;
layout(binding = 0) uniform U { mat3 a; mat3 b; };
void main() { o = (a == b) ? vec4(1.0) : vec4(0.0); }
