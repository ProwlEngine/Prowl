#version 460

layout(triangles) in;
layout(triangle_strip, max_vertices = 6) out;

void main() {
    for (int i = 0; i < 3; ++i) {
        gl_Position = gl_in[i].gl_Position;
        EmitVertex();
    }
    EndPrimitive();

    for (int i = 0; i < 3; ++i) {
        gl_Position = gl_in[i].gl_Position + vec4(0.5, 0.0, 0.0, 0.0);
        EmitVertex();
    }
    EndPrimitive();
}
