#version 460

layout(location = 0) in vec2 inUV;
layout(location = 0) out vec4 fragColor;

void main() {
    float f = fma(inUV.x, inUV.y, 1.0);
    float dx = dFdx(inUV.x);
    float dy = dFdy(inUV.y);
    float w = fwidth(inUV.x);
    fragColor = vec4(f, dx, dy, w);
}
