#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
void main(){ float a[2] = float[2](v.x, 1); o = vec4(a[1]); }
