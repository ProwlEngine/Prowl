#version 450
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    float sum = 0.0;
    for (int i = 0; i < 100; ++i) {
        if (i % 2 == 0) continue;
        if (float(i) > vX * 50.0) break;
        sum += float(i);
    }
    oColor = vec4(sum, 0.0, 0.0, 1.0);
}
