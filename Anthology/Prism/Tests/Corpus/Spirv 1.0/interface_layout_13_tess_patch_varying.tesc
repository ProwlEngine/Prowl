#version 450
layout(vertices=3) out;
layout(location=0) in vec3 vIn[];
layout(location=0) out vec3 vOut[];
layout(location=4) patch out vec2 pOut;
void main(){ vOut[gl_InvocationID]=vIn[gl_InvocationID]; pOut=vec2(1.0); gl_TessLevelOuter[0]=1.0; }
