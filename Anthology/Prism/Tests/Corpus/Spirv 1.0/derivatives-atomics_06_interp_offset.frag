#version 460

layout(location = 0) in vec2 vCoord;

layout(location = 0) out vec4 fragColor;

void main() {
    vec2 c = interpolateAtOffset(vCoord, vec2(0.25, -0.25));
    fragColor = vec4(c, 0.0, 1.0);
}
