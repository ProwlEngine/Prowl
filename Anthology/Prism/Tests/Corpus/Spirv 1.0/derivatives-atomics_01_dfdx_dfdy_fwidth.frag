#version 460

layout(location = 0) in float vScalar;
layout(location = 1) in vec2 vCoord;

layout(location = 0) out vec4 fragColor;

void main() {
    float dx = dFdx(vScalar);
    float dy = dFdy(vScalar);
    float fw = fwidth(vScalar);

    vec2 dxv = dFdx(vCoord);
    vec2 dyv = dFdy(vCoord);
    vec2 fwv = fwidth(vCoord);

    fragColor = vec4(dx + dy + fw, dxv.x + dyv.y, fwv.x, fwv.y);
}
