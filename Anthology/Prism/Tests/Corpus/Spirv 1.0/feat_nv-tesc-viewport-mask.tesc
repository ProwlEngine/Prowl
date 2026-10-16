#version 450
#extension GL_NV_viewport_array2 : require
layout(vertices=3) out;
void main(){ gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position; gl_out[gl_InvocationID].gl_ViewportMask[0] = 1; }
