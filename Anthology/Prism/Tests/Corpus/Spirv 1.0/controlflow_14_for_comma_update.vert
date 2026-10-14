#version 460
void main() {
    int n = gl_VertexIndex;
    float v = 0.0;
    for (int i = 0, j = 0; i < n; i++, j += 2) {
        v += float(j);
    }
    gl_Position = vec4(v, 0.0, 0.0, 1.0);
}
