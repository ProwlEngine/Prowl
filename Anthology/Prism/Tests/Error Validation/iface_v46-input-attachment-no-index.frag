#version 450
layout(binding=0) uniform subpassInput sp;
layout(location=0) out vec4 o;
void main(){ o = subpassLoad(sp); }
