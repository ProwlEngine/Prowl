#version 460
layout(location = 0) in vec3 pos;
layout(binding = 0) uniform U { mat4 m; };
void main() { mat3 r = mat3(m); gl_Position = vec4(r * pos, 1.0); }
