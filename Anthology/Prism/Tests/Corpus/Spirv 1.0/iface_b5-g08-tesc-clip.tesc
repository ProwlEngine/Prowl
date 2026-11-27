#version 450
layout(vertices=4) out;
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[2]; } gl_out[];
void main(){ gl_out[gl_InvocationID].gl_ClipDistance[1] = gl_in[0].gl_Position.x; gl_out[gl_InvocationID].gl_Position = vec4(1); }
