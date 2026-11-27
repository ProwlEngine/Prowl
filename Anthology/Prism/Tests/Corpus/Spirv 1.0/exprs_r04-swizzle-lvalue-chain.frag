#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
void main(){ vec4 a = v; a.zw++; o = a; }
