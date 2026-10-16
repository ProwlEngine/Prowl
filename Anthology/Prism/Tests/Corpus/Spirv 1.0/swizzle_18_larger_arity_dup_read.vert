#version 460
layout(std430, binding=0) buffer B { vec4 items[]; };

void main() {
    int i = gl_VertexIndex;
    vec4 v = items[i];
    vec4 e = v.xxyy * 2.0 + items[i + 1];
    gl_Position = e;
}
