#version 460
layout(location=0) out vec4 fragColor;

void main() {
    int a = (-7) / 2;   // -3
    int b = (-7) % 2;   // -1
    int c = 7 / (-2);   // -3
    int d = 7 % (-2);   // 1
    fragColor = vec4(float(a) / -3.0, float(b) / -1.0, float(c) / -3.0, float(d));
}
