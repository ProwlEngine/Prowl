// a binding on a non opaque non block uniform is not allowed it must be a block
#version 450

layout(binding = 0) uniform float x;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(x);
}
