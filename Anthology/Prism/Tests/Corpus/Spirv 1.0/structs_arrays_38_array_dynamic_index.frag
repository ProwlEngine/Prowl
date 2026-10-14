#version 450
layout(location = 0) flat in int vIndex;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 palette[4] = vec3[4](
        vec3(1.0, 0.0, 0.0),
        vec3(0.0, 1.0, 0.0),
        vec3(0.0, 0.0, 1.0),
        vec3(1.0, 1.0, 0.0));
    int i = clamp(vIndex, 0, 3);
    oColor = vec4(palette[i], 1.0);
}
