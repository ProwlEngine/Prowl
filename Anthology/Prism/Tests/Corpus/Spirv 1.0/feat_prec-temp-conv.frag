#version 450
precision mediump float;
layout(location=0) out vec4 o;
float g(out float x, inout float y){ x = 1.0; y = y * 2.0; return x + y; }
void main(){ double d; float e = 3.0; float r = g(d, e); o = vec4(r, float(d), e, 1.0); }
