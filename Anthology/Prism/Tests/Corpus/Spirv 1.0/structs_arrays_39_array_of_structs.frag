#version 450
struct Wave {
    float amplitude;
    float frequency;
};
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    Wave waves[3];
    waves[0] = Wave(1.0, 2.0);
    waves[1] = Wave(0.5, 4.0);
    waves[2] = Wave(0.25, 8.0);
    float h = 0.0;
    for (int i = 0; i < 3; ++i) {
        h += waves[i].amplitude * sin(vX * waves[i].frequency);
    }
    oColor = vec4(h, 0.0, 0.0, 1.0);
}
