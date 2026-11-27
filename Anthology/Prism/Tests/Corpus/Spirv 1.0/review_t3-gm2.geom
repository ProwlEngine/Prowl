#version 450
layout(invocations=2) in;
layout(points) in;
layout(max_vertices=1) out;
layout(points) out;
void main(){ gl_Position = gl_in[0].gl_Position; EmitVertex(); }
