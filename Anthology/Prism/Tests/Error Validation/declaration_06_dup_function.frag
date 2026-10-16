#version 460

layout(location = 0) out vec4 fragColor;

float f(float x) { return x; }
float f(float x) { return x + 1.0; }

void main() {
    fragColor = vec4(f(1.0));
}
