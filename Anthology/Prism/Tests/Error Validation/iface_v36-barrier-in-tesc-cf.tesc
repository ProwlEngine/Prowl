#version 450
layout(vertices=1) out;
void main(){ if (gl_InvocationID == 0) barrier(); }
