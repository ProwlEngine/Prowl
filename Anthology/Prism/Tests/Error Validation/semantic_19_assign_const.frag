#version 460
layout(location=0) out vec4 o;
void main() {
    const float c = 1.0;
    c = 2.0;
    o = vec4(c);
}
