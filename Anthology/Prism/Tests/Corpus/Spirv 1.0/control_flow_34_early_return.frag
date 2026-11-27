#version 450
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    if (vX < 0.0) {
        oColor = vec4(0.0);
        return;
    }
    oColor = vec4(sqrt(vX), 0.0, 0.0, 1.0);
}
