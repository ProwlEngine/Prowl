#version 450
layout(location=0) in vec4 v;
layout(location=0) out vec4 o;
void main(){ vec4 t = mat4(v.x)[1]; o = t; }
