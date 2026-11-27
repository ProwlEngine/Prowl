#version 460
layout(std430, binding=0) buffer B { vec4 items[]; };

void main() {
    int i = gl_VertexIndex;
    mat4 m = mat4(items[i], items[i + 1], items[i + 2], items[i + 3]);
    vec2 a = m[1].xy;
    gl_Position = vec4(a, 0.0, 1.0);
}
