#version 460
layout(location=0) out vec4 fragColor;

void main() {
    uint a = 1u;
    uint b = 0xFFu;                          // 255
    uint c = 4000000000u + 1000000000u;      // wraps: 705032704u
    fragColor = vec4(float(a), float(b) / 255.0, float(c % 256u) / 255.0, 1.0);
}
