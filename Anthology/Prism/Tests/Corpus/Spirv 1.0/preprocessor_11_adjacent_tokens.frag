#version 460

#define LEFT vec4(1.0
#define RIGHT , 0.5, 0.25, 1.0)

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = LEFT RIGHT;
}
