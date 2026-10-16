#version 450
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    float acc = vX;
    int guard = 0;
    while (acc < 10.0 && guard < 32) {
        acc *= 1.5;
        guard++;
    }
    oColor = vec4(acc, float(guard), 0.0, 1.0);
}
