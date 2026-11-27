#version 460
layout(location=0) out vec4 fragColor;

const float m[2][3] = float[2][3](
    float[3](1.0, 2.0, 3.0),
    float[3](4.0, 5.0, 6.0));

void main() {
    fragColor = vec4(m[0][0], m[0][2] / 3.0, m[1][0] / 4.0, m[1][2] / 6.0);
}
