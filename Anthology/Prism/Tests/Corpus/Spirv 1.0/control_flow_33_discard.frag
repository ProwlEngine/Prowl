#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    if (vUV.x * vUV.x + vUV.y * vUV.y > 1.0) {
        discard;
    }
    oColor = vec4(vUV, 0.0, 1.0);
}
