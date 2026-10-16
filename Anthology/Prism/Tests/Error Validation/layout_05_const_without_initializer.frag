// a const variable must have an initializer
#version 450

const float x;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(x);
}
