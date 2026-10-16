#version 460
layout(location = 0) in vec2 uvs[3];
layout(location = 0) out vec4 o;
void main() { o = vec4(uvs[0] + uvs[1] + uvs[2], 0.0, 1.0); }
