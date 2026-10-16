// an interpolation qualifier cannot be applied to a uniform
#version 450

smooth uniform float x;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(x);
}
