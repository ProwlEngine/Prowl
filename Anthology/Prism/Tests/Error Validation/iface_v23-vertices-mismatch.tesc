#version 450
layout(vertices=3) out;
layout(location=0) out vec4 o[4];
void main(){ o[gl_InvocationID] = vec4(0); }
