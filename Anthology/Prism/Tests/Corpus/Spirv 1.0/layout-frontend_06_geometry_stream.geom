#version 460 core

// Geometry multi-stream output: two vertex streams with per-stream emit.

layout(points) in;
layout(points, max_vertices = 2) out;

layout(location = 0, stream = 0) out vec4 v_stream0;
layout(location = 1, stream = 1) out vec4 v_stream1;

void main()
{
    v_stream0 = gl_in[0].gl_Position;
    gl_Position = gl_in[0].gl_Position;
    EmitStreamVertex(0);
    EndStreamPrimitive(0);

    v_stream1 = gl_in[0].gl_Position * 2.0;
    gl_Position = gl_in[0].gl_Position * 2.0;
    EmitStreamVertex(1);
    EndStreamPrimitive(1);
}
