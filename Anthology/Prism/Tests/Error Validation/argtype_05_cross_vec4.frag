#version 450
// cross called with vec4 arguments but it requires vec3
layout(location=0) in vec4 a;
layout(location=1) in vec4 b;
layout(location=0) out vec4 outColor;
void main() {
    vec3 r = cross(a, b);
    outColor = vec4(r, 1.0);
}
