#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int i;
    uint sh;
};

void main() {
    int a = i << sh;
    int b = i >> sh;
    fragColor = vec4(float(a), float(b), 0.0, 1.0);
}
