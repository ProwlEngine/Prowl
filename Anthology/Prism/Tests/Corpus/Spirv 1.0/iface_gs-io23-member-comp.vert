#version 450
layout(location=0) out B { layout(location=1, component=2) vec2 a; layout(location=1, component=0) vec2 b; } blk;
void main(){ blk.a = vec2(1); blk.b = vec2(2); gl_Position = vec4(0); }
