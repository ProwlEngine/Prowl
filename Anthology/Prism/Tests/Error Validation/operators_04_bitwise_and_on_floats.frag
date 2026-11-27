#version 450
// Bitwise AND applied to floating point operands is not allowed
layout(location=0) out vec4 outColor;
void main() {
    float a = 3.0;
    float b = 5.0;
    float c = a & b;
    outColor = vec4(c);
}
