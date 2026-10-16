#version 450
layout(location = 0) flat in int vI;
layout(location = 0) out vec4 oColor;
void main() {
    int a = vI + 3;
    int b = a * 7;
    int c = b % 5;
    int d = -c;
    oColor = vec4(float(a), float(b), float(c), float(d));
}
