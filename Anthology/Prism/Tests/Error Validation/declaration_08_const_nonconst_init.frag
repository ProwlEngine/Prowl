#version 460

layout(location = 0) in vec4 inVal;
layout(location = 0) out vec4 fragColor;

const vec4 c = inVal;

void main() {
    fragColor = c;
}
