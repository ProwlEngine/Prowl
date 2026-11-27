#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int i;
    float f;
};

void main() {
    float r = i + f;
    fragColor = vec4(r);
}
