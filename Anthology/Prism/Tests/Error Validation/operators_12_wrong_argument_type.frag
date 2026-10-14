#version 450
// User function expects a float but is passed a vec3 with no implicit conversion
layout(location=0) out vec4 outColor;
float scale(float a) {
    return a * 2.0;
}
void main() {
    float r = scale(vec3(1.0, 2.0, 3.0));
    outColor = vec4(r);
}
