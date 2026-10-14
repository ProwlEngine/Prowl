#version 460

layout(location = 0) out vec4 fragColor;

float a;
float b;
float c;

a = b + c;

void main() {
    fragColor = vec4(a);
}
