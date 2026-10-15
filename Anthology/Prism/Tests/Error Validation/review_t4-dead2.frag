#version 450
layout(location=0) out vec4 c;
void g(){ undefinedVar = 3; }
void main(){ c = vec4(1); }
