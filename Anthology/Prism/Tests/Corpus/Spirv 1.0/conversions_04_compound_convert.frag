#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int i;
    float f0;
};

void main() {
    float f = f0;
    f += i;
    f *= i;
    fragColor = vec4(f);
}
