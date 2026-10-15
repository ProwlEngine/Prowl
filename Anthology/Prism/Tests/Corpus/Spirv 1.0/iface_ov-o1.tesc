#version 450
layout(vertices=3) out;
layout(location=0) out vec4 a[3];
layout(location=1) out vec4 b[3];
layout(location=0) in vec4 x[gl_MaxPatchVertices];
layout(location=1) in vec4 y[];
void main(){ a[gl_InvocationID] = x[0]; b[gl_InvocationID] = y[1]; }
