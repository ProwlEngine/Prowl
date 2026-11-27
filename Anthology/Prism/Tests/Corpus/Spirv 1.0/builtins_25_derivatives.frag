#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    vec2 dx = dFdx(vUV);
    vec2 dy = dFdy(vUV);
    vec2 fw = fwidth(vUV);
    oColor = vec4(dx, dy) + vec4(fw, 0.0, 0.0);
}
