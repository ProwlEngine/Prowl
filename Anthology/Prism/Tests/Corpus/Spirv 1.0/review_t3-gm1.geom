#version 450
layout(triangles) in;
layout(invocations=4) in;
layout(triangle_strip) out;
layout(max_vertices=3) out;
void main(){ gl_Position = gl_in[0].gl_Position; EmitVertex(); }
