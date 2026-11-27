#version 460
layout(location=0) out vec4 o;
void main() {
    mat3 m = mat3(1.0);
    vec4 v = vec4(1.0);
    vec3 r = m * v;
    o = vec4(r, 1.0);
}
