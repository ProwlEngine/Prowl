// sqrt takes 1 argument but is called with 2
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in float a;
layout(location=1) in float b;
void main() {
    float r = sqrt(a, b);
    outColor = vec4(r, r, r, 1.0);
}
