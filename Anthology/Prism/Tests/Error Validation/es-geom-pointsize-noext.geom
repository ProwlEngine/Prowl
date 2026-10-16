#version 320 es
layout(points) in;
layout(points, max_vertices=1) out;
void main(){ gl_Position = gl_in[0].gl_Position; gl_PointSize = 2.0; EmitVertex(); }
