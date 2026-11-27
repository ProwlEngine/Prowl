#version 460 core

// exercises lines in/out geometry stage with gl_PrimitiveIDIn

layout(lines) in;
layout(line_strip, max_vertices = 2) out;

layout(location = 0) flat out int primitive_id;

void main()
{
    primitive_id = gl_PrimitiveIDIn;

    gl_Position = gl_in[0].gl_Position;
    EmitVertex();

    gl_Position = gl_in[1].gl_Position;
    EmitVertex();

    EndPrimitive();
}
