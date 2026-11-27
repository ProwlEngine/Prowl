#version 460
layout(binding = 0) uniform U { mat3x2 b; mat2x3 a; };
void main() { mat2 r = b * a; gl_Position = vec4(r[0], r[1]); }
