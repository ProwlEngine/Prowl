#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    float sum = 0.0;
    for (int y = 0; y < 4; ++y) {
        for (int x = 0; x < 4; ++x) {
            sum += vUV.x * float(x) + vUV.y * float(y);
        }
    }
    oColor = vec4(sum / 16.0, 0.0, 0.0, 1.0);
}
