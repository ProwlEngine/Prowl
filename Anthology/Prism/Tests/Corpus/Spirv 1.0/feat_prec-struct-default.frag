#version 310 es
precision mediump float;
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
struct L { vec3 c; float k; };
L make(vec4 x) { L l; l.c = x.xyz; l.k = x.w; return l; }
void main(){ L a = make(v); L arr[2]; arr[0] = a; arr[1] = make(v * 2.0); o = vec4(arr[1].c * a.k, arr[0].k); }
