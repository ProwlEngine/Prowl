#version 460
layout(location=0) out vec4 o;
void main() {
    int n = 3;
    float a[n];
    a[0] = 1.0;
    o = vec4(a[0]);
}
