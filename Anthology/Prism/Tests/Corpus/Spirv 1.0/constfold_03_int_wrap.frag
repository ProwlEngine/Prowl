#version 460
layout(location=0) out vec4 fragColor;

void main() {
    int a = 2147483647 + 1;   // wraps to -2147483648
    fragColor = vec4(float(a) / -2147483648.0, 0.0, 0.0, 1.0);
}
