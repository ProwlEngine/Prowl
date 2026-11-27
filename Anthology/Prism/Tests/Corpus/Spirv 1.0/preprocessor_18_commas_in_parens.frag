#version 460

#define WHITE vec4(1.0, 1.0, 1.0, 1.0)
#define BLEND(t) mix(vec4(0.0, 0.0, 0.0, 1.0), WHITE, t)

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = BLEND(0.5);
}
