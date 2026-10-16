#version 450
// default label used outside of any switch statement
layout(location=0) out vec4 outColor;

void main() {
    int x = 3;
    default:
    outColor = vec4(float(x));
}
