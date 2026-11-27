#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
layout(location=2) flat in uint u;
const uint a = 3000000000; void main(){ o = vec4(a > 5u); }
