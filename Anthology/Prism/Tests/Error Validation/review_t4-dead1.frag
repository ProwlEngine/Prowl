#version 450
layout(location=0) out vec4 c;
float f(){ int x = vec3(1); return 1.0; }
void main(){ c = vec4(1); }
