#version 320 es
#extension GL_EXT_tessellation_point_size : require
layout(vertices=1) out;
void main(){ gl_out[gl_InvocationID].gl_PointSize = gl_in[0].gl_PointSize; }
