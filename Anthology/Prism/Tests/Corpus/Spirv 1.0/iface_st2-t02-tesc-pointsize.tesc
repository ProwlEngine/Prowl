#version 450
layout(vertices=1) out;
void main(){ gl_out[gl_InvocationID].gl_PointSize = gl_in[0].gl_PointSize; gl_out[gl_InvocationID].gl_ClipDistance[0] = 1.0; }
