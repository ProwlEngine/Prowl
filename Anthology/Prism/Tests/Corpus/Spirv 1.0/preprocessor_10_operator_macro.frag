#version 460

#define PLUS +
#define HALF 0.5 PLUS 0.25

layout(location = 0) out vec4 fragColor;

void main() {
    float x = HALF;
    fragColor = vec4(x, 0.0, 0.0, 1.0);
}
