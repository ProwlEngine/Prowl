#version 460
layout(location = 0) out vec4 fragColor;
void main() {
    int n = int(gl_FragCoord.x) & 7;
    int i = 0;
    float v = 0.0;
    do {
        v += 1.0;
        i++;
    } while (i < n);
    fragColor = vec4(v);
}
