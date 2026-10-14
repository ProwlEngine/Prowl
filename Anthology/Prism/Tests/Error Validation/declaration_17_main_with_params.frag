#version 460

layout(location = 0) out vec4 fragColor;

void main(int x) {
    fragColor = vec4(float(x));
}
