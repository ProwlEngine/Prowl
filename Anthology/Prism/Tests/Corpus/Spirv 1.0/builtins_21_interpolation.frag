#version 450
layout(location = 0) in float vT;
layout(location = 0) out vec4 oColor;
void main() {
    float m = mix(0.0, 1.0, vT);
    float s = step(0.5, vT);
    float ss = smoothstep(0.0, 1.0, vT);
    oColor = vec4(m, s, ss, 1.0);
}
