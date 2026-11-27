#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int i;
    int j;
};

void main() {
    float f = i * j - i;
    fragColor = vec4(f);
}
