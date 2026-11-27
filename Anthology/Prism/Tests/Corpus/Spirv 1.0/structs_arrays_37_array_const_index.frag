#version 450
layout(location = 0) in float vT;
layout(location = 0) out vec4 oColor;
void main() {
    float weights[4];
    weights[0] = 0.1;
    weights[1] = 0.2;
    weights[2] = 0.3;
    weights[3] = 0.4;
    float sum = weights[0] + weights[1] + weights[2] + weights[3];
    oColor = vec4(sum * vT, 0.0, 0.0, 1.0);
}
