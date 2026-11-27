#version 450
layout(location=0) in vec4 v;
layout(location=0) out vec4 o;
void main(){ float a[2] = float[2](v.x, v.y); o = vec4(float[2](v.x,v.y)[1]); }
