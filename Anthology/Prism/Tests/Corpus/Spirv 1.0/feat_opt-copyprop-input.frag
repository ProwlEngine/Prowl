#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 cols[3];
layout(location=3) flat in int k;
void main(){ vec4 local[3] = cols; o = local[k] + local[0]; }
