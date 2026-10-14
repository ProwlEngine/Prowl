#version 460
layout(location = 0) out vec4 fragColor;
void main() {
    int n = int(gl_FragCoord.x) & 1;
    if (n == 0) {
        fragColor = vec4(1.0);
        return;
        fragColor = vec4(0.5);
    }
    fragColor = vec4(0.0);
}
