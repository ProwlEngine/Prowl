#version 460
layout(location=0) out vec4 o;
void main() {
    float f = 1.0;
    float g = f[0];
    o = vec4(g);
}
