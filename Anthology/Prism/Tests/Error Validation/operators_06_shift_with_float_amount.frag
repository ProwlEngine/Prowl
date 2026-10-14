#version 450
// Left shift amount is a float but shift operands must be integer types
layout(location=0) out vec4 outColor;
void main() {
    int a = 1;
    float b = 2.0;
    int c = a << b;
    outColor = vec4(float(c));
}
