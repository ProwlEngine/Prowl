#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
void main(){ vec3 a = v.xyz + ivec3(i); o = vec4(a, 1); }
