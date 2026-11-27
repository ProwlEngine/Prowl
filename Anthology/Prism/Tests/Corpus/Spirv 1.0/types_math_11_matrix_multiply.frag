#version 450
layout(location = 0) in vec4 vPos;
layout(location = 1) in mat4 vMat;
layout(location = 0) out vec4 oColor;
void main() {
    oColor = vMat * vPos;
}
