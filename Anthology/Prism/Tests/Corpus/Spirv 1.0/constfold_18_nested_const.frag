#version 460
layout(location=0) out vec4 fragColor;

const float A = 2.0;
const float B = A * 3.0;    // 6
const float C = B + A;      // 8
const vec2 D = vec2(A, C);  // (2, 8)

void main() {
    fragColor = vec4(A / 2.0, B / 6.0, C / 8.0, D.y / 8.0);
}
