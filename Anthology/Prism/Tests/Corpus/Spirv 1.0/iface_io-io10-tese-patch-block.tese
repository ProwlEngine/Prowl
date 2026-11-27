#version 450
layout(isolines) in;
layout(location=0) patch in PB { vec4 a; float b[2]; } pb;
layout(location=4) in VB { vec3 c; } vb[];
void main(){ gl_Position = pb.a + vec4(vb[1].c, pb.b[1]); }
