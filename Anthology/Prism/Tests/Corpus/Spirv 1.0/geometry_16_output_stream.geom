#version 460
#extension GL_ARB_gpu_shader5 : require

layout(points) in;
layout(points, max_vertices = 1, stream = 0) out;

void main() {
    gl_Position = gl_in[0].gl_Position;
    EmitStreamVertex(0);
    EndStreamPrimitive(0);
}
