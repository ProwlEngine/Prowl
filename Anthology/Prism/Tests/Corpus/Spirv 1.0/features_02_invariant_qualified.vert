#version 460
layout(location = 0) in vec3 pos;
invariant layout(location = 0) out vec3 vNormal;
void main() {
    vNormal = pos;
    gl_Position = vec4(pos, 1.0);
}
