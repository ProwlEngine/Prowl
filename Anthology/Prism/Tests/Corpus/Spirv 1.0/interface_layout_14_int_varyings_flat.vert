#version 450
layout(location=0) in vec4 p;
layout(location=0) flat out ivec3 iv;
layout(location=1) flat out uvec2 uv;
void main(){ iv=ivec3(1); uv=uvec2(2u); gl_Position=p; }
