#version 460
layout(location = 0) out vec4 fragColor;
void main() {
    int n = int(gl_FragCoord.x) & 3;
    if (n == 0) {
        fragColor = vec4(0.0);
        return;
    }
    float v = float(n) * 0.25;
    fragColor = vec4(v);
}
