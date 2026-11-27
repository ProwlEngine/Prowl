#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
void main(){ mat2 m = mat2(v); m = 1.0 / m; o = vec4(m[0], m[1]); }
