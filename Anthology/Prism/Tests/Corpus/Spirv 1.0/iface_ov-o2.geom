#version 450
layout(triangles) in;
layout(points, max_vertices=1) out;
layout(location=0) in vec4 a[3];
layout(location=1) in vec4 b[3];
void main(){ gl_Position = a[0] + b[2]; EmitVertex(); }
