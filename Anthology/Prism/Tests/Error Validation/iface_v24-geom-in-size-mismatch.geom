#version 450
layout(triangles) in;
layout(points, max_vertices=1) out;
layout(location=0) in vec4 v[2];
void main(){ gl_Position = v[0]; EmitVertex(); }
