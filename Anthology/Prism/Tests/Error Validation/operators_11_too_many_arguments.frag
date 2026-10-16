#version 450
// User function is called with three arguments but it declares only two
layout(location=0) out vec4 outColor;
float addTwo(float a, float b) {
    return a + b;
}
void main() {
    float r = addTwo(1.0, 2.0, 3.0);
    outColor = vec4(r);
}
