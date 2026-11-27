#version 460
layout(location=0) out vec4 o;
void main() {
    vec2 a = vec2(1.0);
    vec3 c = cross(a, a);
    o = vec4(c, 1.0);
}
