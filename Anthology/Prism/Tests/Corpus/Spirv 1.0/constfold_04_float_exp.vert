#version 460

void main() {
    float a = 1e3;      // 1000.0
    float b = 1.5e-2;   // 0.015
    float c = .5;       // 0.5
    float d = 5.;       // 5.0
    gl_Position = vec4(a / 1000.0, b / 0.015, c, d / 5.0);
}
