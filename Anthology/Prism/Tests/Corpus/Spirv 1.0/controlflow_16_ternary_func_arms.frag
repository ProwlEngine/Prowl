#version 460
layout(location = 0) out vec4 fragColor;
float heavyA(float x) { return sin(x) * cos(x) + x * 0.5; }
float heavyB(float x) { return sqrt(abs(x)) + x * x; }
void main() {
    float x = gl_FragCoord.x;
    int n = int(gl_FragCoord.y) & 1;
    float v = (n == 0) ? heavyA(x) : heavyB(x);
    fragColor = vec4(v);
}
