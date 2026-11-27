#version 450
layout(points) in;
layout(points, max_vertices=2) out;
layout(location=0, stream=1) out B { vec4 a; } blk;
void main(){ blk.a = vec4(1); EmitStreamVertex(1); gl_Position = vec4(0); EmitStreamVertex(0); }
