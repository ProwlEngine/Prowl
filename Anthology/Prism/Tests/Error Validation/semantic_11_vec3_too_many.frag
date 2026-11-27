#version 460
layout(location=0) out vec4 o;
void main() {
    vec3 v = vec3(1.0, 2.0, 3.0, 4.0);
    o = vec4(v, 1.0);
}
