#version 460
layout(location = 0) in vec3 pos;
layout(location = 0) out vec3 vColor;
invariant vColor;
void main() {
    vColor = pos;
    gl_Position = vec4(pos, 1.0);
}
