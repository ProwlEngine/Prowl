#version 450
// length() called on a float which is not an array
layout(location=0) out vec4 outColor;

void main() {
    float f = 3.0;
    int n = f.length();
    outColor = vec4(float(n));
}
