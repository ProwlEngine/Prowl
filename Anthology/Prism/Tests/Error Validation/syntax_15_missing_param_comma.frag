#version 460

layout(location = 0) out vec4 fragColor;

float add(float a float b) {
    return a + b;
}

void main() {
    fragColor = vec4(add(1.0, 2.0));
}
