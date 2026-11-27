#version 460

layout(location = 0) out vec4 fragColor;

void main() {
    float a = 1.0 + * 2.0;
    fragColor = vec4(a);
}
