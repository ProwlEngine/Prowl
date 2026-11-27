#version 320 es
#extension GL_OES_tessellation_point_size : require
layout(triangles) in;
void main(){ gl_Position = gl_in[0].gl_Position * gl_TessCoord.x; gl_PointSize = gl_in[1].gl_PointSize; }
