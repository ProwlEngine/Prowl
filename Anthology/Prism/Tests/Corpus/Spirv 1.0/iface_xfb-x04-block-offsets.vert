#version 450
layout(location=0, xfb_buffer=2, xfb_offset=8) out B { vec4 a; float b; layout(xfb_offset=40) vec2 c; } blk;
void main(){ blk.a = vec4(1); blk.b = 2.0; blk.c = vec2(0); gl_Position = vec4(0); }
