#version 450
layout(location = 0) in vec3 vColor;
layout(location = 0) out vec4 oColor;

float square(float x) { return x * x; }
vec3 square(vec3 x) { return x * x; }

void main() {
    vec3 a = square(vColor);
    float b = square(vColor.r);
    oColor = vec4(a, b);
}
