#version 460
layout(location=0) out vec4 fragColor;

void main() {
    int a = 0xF0 & 0x3C;   // 48
    int b = 0x0F | 0x30;   // 63
    int c = 0xFF ^ 0x0F;   // 240
    int d = (~0) & 0xFF;   // 255
    int e = 1 << 4;        // 16
    int f = 256 >> 3;      // 32
    fragColor = vec4(float(a + b) / 111.0, float(c + d) / 495.0, float(e) / 16.0, float(f) / 32.0);
}
