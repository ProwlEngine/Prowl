#version 460

layout(push_constant, set = 0, binding = 0) uniform Push {
    vec4 color;
} pc;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = pc.color;
}
