#version 450
layout(location=0) out vec4 c;
void main(){ c = vec4(dFdx(1.0)); }
