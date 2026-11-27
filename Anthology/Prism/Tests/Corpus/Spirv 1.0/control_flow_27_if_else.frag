#version 450
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 c;
    if (vX > 0.66) {
        c = vec3(1.0, 0.0, 0.0);
    } else if (vX > 0.33) {
        c = vec3(0.0, 1.0, 0.0);
    } else {
        c = vec3(0.0, 0.0, 1.0);
    }
    oColor = vec4(c, 1.0);
}
