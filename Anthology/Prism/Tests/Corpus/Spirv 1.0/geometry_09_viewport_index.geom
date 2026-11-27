#version 460
#extension GL_ARB_viewport_array : require

layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;

void main() {
    for (int i = 0; i < 3; ++i) {
        gl_Position = gl_in[i].gl_Position;
        gl_ViewportIndex = 1;
        EmitVertex();
    }
    EndPrimitive();
}
