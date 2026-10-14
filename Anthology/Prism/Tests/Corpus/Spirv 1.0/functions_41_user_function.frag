#version 450
layout(location = 0) in vec3 vColor;
layout(location = 0) out vec4 oColor;

float luminance(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

void main() {
    float l = luminance(vColor);
    oColor = vec4(vec3(l), 1.0);
}
