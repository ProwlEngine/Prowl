#version 450
// Classic attributeless fullscreen triangle driven by gl_VertexIndex.
layout(location = 0) out vec2 vUV;
void main() {
    vec2 pos = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    vUV = pos;
    gl_Position = vec4(pos * 2.0 - 1.0, 0.0, 1.0);
}
