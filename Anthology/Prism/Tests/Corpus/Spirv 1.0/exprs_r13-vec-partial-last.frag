#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
void main(){ o = vec4(vec3(v.xy, v.zw), 1); }
