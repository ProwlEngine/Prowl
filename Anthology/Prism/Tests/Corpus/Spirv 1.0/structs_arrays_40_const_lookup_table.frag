#version 450
const vec2 kOffsets[4] = vec2[4](
    vec2(-1.0, -1.0),
    vec2( 1.0, -1.0),
    vec2(-1.0,  1.0),
    vec2( 1.0,  1.0));
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    vec2 acc = vec2(0.0);
    for (int i = 0; i < 4; ++i) {
        acc += vUV + kOffsets[i] * 0.01;
    }
    oColor = vec4(acc * 0.25, 0.0, 1.0);
}
