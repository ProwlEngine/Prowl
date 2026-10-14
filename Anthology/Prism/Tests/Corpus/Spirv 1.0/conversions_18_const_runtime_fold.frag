#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int r;
};

void main() {
    const int K = 5;
    const float KF = 2.0;
    int a = K + r;
    float b = KF * float(r) + 3.0;
    fragColor = vec4(float(a), b, 0.0, 1.0);
}
