#version 450

layout(location = 0) out vec4 colorA;
layout(location = 0) out vec4 colorB;

void main() {
    colorA = vec4(1.0);
    colorB = vec4(0.0);
    gl_Position = vec4(0.0);
}
