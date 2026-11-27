#version 450
layout(location = 0) in float vT;
layout(location = 0) out vec4 oColor;
void main() {
    float a = vT + 1.0;
    float b = a * 2.0;
    float c = b - 0.5;
    float d = c / 3.0;
    oColor = vec4(d, a, b, c);
}
