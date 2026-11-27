#version 460

layout(location = 0) out vec4 fragColor;

void main() {
    int for = 3;
    fragColor = vec4(float(for));
}
