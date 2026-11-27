#version 450

layout(location = 0) in vec4 inColor;
layout(location = 0) out vec4 outColor;

void main() {
    inColor = vec4(1.0);
    outColor = inColor;
}
