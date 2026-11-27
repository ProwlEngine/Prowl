#version 460
layout(location=0) out vec4 o;
void main() {
    float f = vec3(1.0);
    o = vec4(f);
}
