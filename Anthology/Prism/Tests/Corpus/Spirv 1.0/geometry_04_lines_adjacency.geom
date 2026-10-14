#version 460

layout(lines_adjacency) in;
layout(line_strip, max_vertices = 2) out;

void main() {
    gl_Position = gl_in[1].gl_Position;
    EmitVertex();
    gl_Position = gl_in[2].gl_Position;
    EmitVertex();
    EndPrimitive();
}
