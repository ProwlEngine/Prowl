#version 450
layout(points) in;
layout(points, max_vertices=1) out;
layout(stream=1) out;
layout(location=0) out float x;
void main(){ x = 1.0; gl_Position = gl_in[0].gl_Position; EmitStreamVertex(0); }
