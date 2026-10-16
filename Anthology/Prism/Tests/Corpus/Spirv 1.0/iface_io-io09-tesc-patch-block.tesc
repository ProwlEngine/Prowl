#version 450
layout(vertices=2) out;
layout(location=0) patch out PB { vec4 a; float b[2]; } pb;
layout(location=4) out VB { vec3 c; } vb[];
void main(){ if (gl_InvocationID == 0) { pb.a = vec4(1); pb.b[1] = 2.0; } vb[gl_InvocationID].c = vec3(3); gl_TessLevelOuter[1] = 1.0; }
