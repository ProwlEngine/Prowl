#version 450
layout(location = 0) flat in int vMode;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 c;
    switch (vMode) {
        case 0: c = vec3(1.0, 0.0, 0.0); break;
        case 1: c = vec3(0.0, 1.0, 0.0); break;
        case 2:
        case 3: c = vec3(0.0, 0.0, 1.0); break;
        default: c = vec3(0.5); break;
    }
    oColor = vec4(c, 1.0);
}
