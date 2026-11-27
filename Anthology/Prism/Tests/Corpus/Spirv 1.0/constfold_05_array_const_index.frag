#version 460
layout(location=0) out vec4 fragColor;

const float arr[4] = float[4](1.0 + 1.0, 2.0 * 3.0, 10.0 / 4.0, sqrt(16.0));

void main() {
    fragColor = vec4(arr[0] / 2.0, arr[1] / 6.0, arr[2] / 2.5, arr[3] / 4.0);
}
