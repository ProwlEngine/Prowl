#version 460
layout(std430, binding=0) buffer B { vec4 items[]; };

void main() {
    int i = gl_VertexIndex;
    mat4 m = mat4(items[i], items[i + 1], items[i + 2], items[i + 3]);
    int idx = int(items[i].w) & 3;
    float f = m[idx].y;
    gl_Position = vec4(f);
}
