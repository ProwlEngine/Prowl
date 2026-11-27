// two fragment inputs share the same explicit location 0 which overlaps
#version 450

layout(location = 0) in vec4 a;
layout(location = 0) in vec4 b;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = a + b;
}
