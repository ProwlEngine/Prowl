#version 450
// Logical AND applied to integer operands but it requires bool operands
layout(location=0) out vec4 outColor;
void main() {
    int a = 1;
    int b = 0;
    bool r = a && b;
    outColor = vec4(r ? 1.0 : 0.0);
}
