#version 460
void main() {
    int n = gl_VertexIndex;
    float v = 0.0;
    for (int i = 0; i < 10; i++) {
        if (i == n) break;
        v += 1.0;
    }
    gl_Position = vec4(v, 0.0, 0.0, 1.0);
}
