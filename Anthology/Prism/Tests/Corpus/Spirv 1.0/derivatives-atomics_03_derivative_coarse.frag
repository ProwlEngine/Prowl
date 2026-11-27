#version 460
#extension GL_ARB_derivative_control : require

layout(location = 0) in float vScalar;
layout(location = 1) in vec2 vCoord;

layout(location = 0) out vec4 fragColor;

void main() {
    float dx = dFdxCoarse(vScalar);
    float dy = dFdyCoarse(vScalar);
    float fw = fwidthCoarse(vScalar);

    vec2 dxv = dFdxCoarse(vCoord);
    vec2 dyv = dFdyCoarse(vCoord);
    vec2 fwv = fwidthCoarse(vCoord);

    fragColor = vec4(dx + dy + fw, dxv.x + dyv.y, fwv.x, fwv.y);
}
