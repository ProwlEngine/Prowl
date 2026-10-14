#version 460
layout(location = 0) in vec3 pos;
layout(binding = 0) uniform U { mat3 m; };
void main() { mat4 r = mat4(m); gl_Position = r * vec4(pos, 1.0); }
