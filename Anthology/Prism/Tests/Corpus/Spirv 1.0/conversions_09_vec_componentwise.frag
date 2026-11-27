#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    vec4 a;
    vec4 b;
};

void main() {
    vec4 prod = a * b;
    fragColor = prod;
}
