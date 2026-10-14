#version 460

void main() {
    float v = float(__VERSION__);
    float l = float(__LINE__);
    gl_Position = vec4(v / 1000.0, l / 100.0, 0.0, 1.0);
}
