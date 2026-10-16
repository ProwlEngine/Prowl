#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
void main(){ dmat2 a = dmat2(v.x); dmat2 b = a + a; o = vec4(vec2(b[0]), 0, 0); }
