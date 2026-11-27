#version 310 es
precision mediump float;
layout(location=0) in Inputs { vec4 color; } ins;
layout(location=0) out vec4 o;
void main(){ o = ins.color; }
