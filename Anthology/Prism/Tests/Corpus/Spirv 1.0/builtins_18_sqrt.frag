#version 450
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    float s = sqrt(vX);
    float i = inversesqrt(vX + 1.0);
    oColor = vec4(s, i, s * i, 1.0);
}
