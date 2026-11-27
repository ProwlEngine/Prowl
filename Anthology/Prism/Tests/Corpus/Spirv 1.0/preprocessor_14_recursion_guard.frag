#version 460

#define GLOW GLOW

layout(location = 0) out vec4 fragColor;

void main() {
    float GLOW = 0.6;
    fragColor = vec4(GLOW, GLOW * 0.5, 0.0, 1.0);
}
