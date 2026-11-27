#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform BlockA {
    vec4 color;
} value;

layout(binding = 1) uniform BlockB {
    vec4 value;
};

void main() {
    fragColor = value.color;
}
