#version 450
layout(location=0) in B { layout(location=5) vec4 a; vec4 b; } blk;
layout(location=0) out vec4 o;
void main(){ o = blk.a + blk.b; }
