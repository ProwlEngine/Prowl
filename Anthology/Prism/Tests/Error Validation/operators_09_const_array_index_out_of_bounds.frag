#version 450
// Constant array index 5 is out of bounds for an array of size 3
layout(location=0) out vec4 outColor;
void main() {
    float a[3] = float[3](1.0, 2.0, 3.0);
    float f = a[5];
    outColor = vec4(f);
}
