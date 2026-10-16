#version 460
#extension GL_ARB_derivative_control : require

layout(location = 0) in float vScalar;
layout(location = 1) in vec2 vCoord;

layout(location = 0) out vec4 fragColor;

void main() {
    float dx = dFdxFine(vScalar);
    float dy = dFdyFine(vScalar);
    float fw = fwidthFine(vScalar);

    vec2 dxv = dFdxFine(vCoord);
    vec2 dyv = dFdyFine(vCoord);
    vec2 fwv = fwidthFine(vCoord);

    fragColor = vec4(dx + dy + fw, dxv.x + dyv.y, fwv.x, fwv.y);
}
