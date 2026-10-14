#version 450
// Modulo operator applied to floating point operands is not allowed
layout(location=0) out vec4 outColor;
void main() {
    float a = 5.0;
    float b = 2.0;
    float c = a % b;
    outColor = vec4(c);
}
