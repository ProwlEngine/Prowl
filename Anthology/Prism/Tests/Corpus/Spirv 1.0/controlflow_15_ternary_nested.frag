#version 460
layout(location = 0) out vec4 fragColor;
void main() {
    int n = int(gl_FragCoord.x) & 3;
    float v = (n == 0) ? 1.0 : ((n == 1) ? 2.0 : ((n == 2) ? 3.0 : 4.0));
    fragColor = vec4(v);
}
