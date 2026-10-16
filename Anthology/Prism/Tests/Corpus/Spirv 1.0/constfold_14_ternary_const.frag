#version 460
layout(location=0) out vec4 fragColor;

void main() {
    const int k = 5;
    float v = (k > 3) ? 0.25 : 0.75;   // 0.25
    fragColor = vec4(v, v * 2.0, v * 3.0, 1.0);
}
