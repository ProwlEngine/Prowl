#version 450
// case label used outside of any switch statement
layout(location=0) out vec4 outColor;

void main() {
    int x = 3;
    case 1:
    outColor = vec4(float(x));
}
