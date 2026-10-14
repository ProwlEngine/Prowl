#version 450
// dot called with vec3 and vec4 but both arguments must be the same size
layout(location=0) in vec3 v3;
layout(location=1) in vec4 v4;
layout(location=0) out vec4 outColor;
void main() {
    float r = dot(v3, v4);
    outColor = vec4(r);
}
