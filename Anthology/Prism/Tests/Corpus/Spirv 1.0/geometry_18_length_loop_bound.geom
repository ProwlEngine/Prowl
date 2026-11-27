#version 460

layout(lines) in;
layout(line_strip, max_vertices = 2) out;

void main() {
    int n = gl_in.length();
    for (int i = 0; i < n; ++i) {
        gl_Position = gl_in[i].gl_Position * 2.0;
        EmitVertex();
    }
    EndPrimitive();
}
