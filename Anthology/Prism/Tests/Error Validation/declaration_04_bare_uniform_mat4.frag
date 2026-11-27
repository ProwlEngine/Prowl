#version 460

uniform mat4 uTransform;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = uTransform[0];
}
