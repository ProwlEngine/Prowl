#version 450
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    float acc = vX;
    int i = 0;
    do {
        acc += 0.1;
        i++;
    } while (acc < 1.0 && i < 64);
    oColor = vec4(acc, float(i), 0.0, 1.0);
}
