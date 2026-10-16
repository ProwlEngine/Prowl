#version 310 es
layout(location=0) out Outs { vec4 c; } o;
void main(){ o.c = vec4(1.0); gl_Position = vec4(0.0); }
