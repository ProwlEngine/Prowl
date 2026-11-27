#version 460

#define BRIGHTNESS 0.75
#define TINT (BRIGHTNESS * 0.5)

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(BRIGHTNESS, TINT, 0.25, 1.0);
}
