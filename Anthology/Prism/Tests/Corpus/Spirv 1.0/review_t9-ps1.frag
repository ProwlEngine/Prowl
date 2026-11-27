#version 310 es
precision mediump float;
layout(location=0) in vec4 v;
float a(float x){ return x*x; }
precision highp float;
layout(location=0) out vec4 c;
void main(){ float t = v.x*v.y; c = vec4(a(t)); }
