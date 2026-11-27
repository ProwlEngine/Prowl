#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int a;
    int b;
    uint c;
    uint d;
};

void main() {
    int m = a % b;
    int q = a / b;
    uint um = c % d;
    uint uq = c / d;
    fragColor = vec4(float(m), float(q), float(um), float(uq));
}
