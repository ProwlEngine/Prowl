#version 450
layout(triangles) in;
layout(location=0) in vec4 a[];
layout(location=1) in vec4 b[gl_MaxPatchVertices];
void main(){ gl_Position = a[0] + b[2]; }
