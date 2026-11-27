#version 460
layout(location=0) out vec4 o;
void main() {
    float s = 0.0;
    for (int i = 0; i + 1; i++) {
        s += 1.0;
    }
    o = vec4(s);
}
