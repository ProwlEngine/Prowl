#version 460
layout(location = 0) out vec4 fragColor;
void main() {
    int n = int(gl_FragCoord.x) & 3;
    float v = 0.0;
    switch (n) {
        case 0: v += 1.0;
        case 1: v += 2.0; break;
        case 2: v += 4.0;
        default: v += 8.0; break;
    }
    fragColor = vec4(v);
}
