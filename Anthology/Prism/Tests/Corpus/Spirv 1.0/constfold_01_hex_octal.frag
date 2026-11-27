#version 460
layout(location=0) out vec4 fragColor;

void main() {
    int a = 0xFF;         // 255
    int b = 0x7FFFFFFF;   // 2147483647
    int c = 0755;         // octal 493
    fragColor = vec4(float(a) / 255.0, float(c) / 493.0, float(b) / 2147483647.0, 1.0);
}
