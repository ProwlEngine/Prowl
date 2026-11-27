#version 450
layout(location = 0) in vec3 vDir;
layout(location = 0) out vec4 oColor;
void main() {
    mat3 m = mat3(
        1.0, 0.0, 0.0,
        0.0, 1.0, 0.0,
        0.0, 0.0, 1.0);
    vec3 r = m * vDir;
    oColor = vec4(r, 1.0);
}
