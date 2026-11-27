#version 450
// dot called with vec2 and vec3 but both arguments must be the same size
layout(location=0) in vec2 v2;
layout(location=1) in vec3 v3;
layout(location=0) out vec4 outColor;
void main() {
    float r = dot(v2, v3);
    outColor = vec4(r);
}
