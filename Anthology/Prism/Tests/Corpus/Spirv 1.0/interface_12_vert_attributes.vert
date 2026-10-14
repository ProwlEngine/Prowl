#version 460

layout(location = 0) in vec4 a;
layout(location = 1) in vec4 b;

void main() {
    gl_Position = a + b;
}
