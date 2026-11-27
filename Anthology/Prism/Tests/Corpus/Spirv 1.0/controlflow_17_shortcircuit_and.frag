#version 460
layout(location = 0) out vec4 fragColor;
bool expensive(float x) { return sin(x) > 0.0; }
void main() {
    float x = gl_FragCoord.x;
    int n = int(gl_FragCoord.y) & 1;
    float v = 0.0;
    if (n == 0 && expensive(x)) {
        v = 1.0;
    }
    fragColor = vec4(v);
}
