#version 320 es
#extension GL_EXT_geometry_point_size : require
layout(points) in;
layout(points, max_vertices=1) out;
void main(){ gl_Position = gl_in[0].gl_Position; gl_PointSize = gl_in[0].gl_PointSize; EmitVertex(); }
