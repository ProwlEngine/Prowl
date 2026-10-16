#version 320 es
layout(triangles) in;
void main(){ gl_Position = vec4(gl_in[0].gl_PointSize); }
