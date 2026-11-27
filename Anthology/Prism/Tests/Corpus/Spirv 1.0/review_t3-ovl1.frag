#version 450
layout(location=0) out vec4 c;
float sin(int x){ return float(x)*2.0; }
void main(){ c = vec4(sin(3)); }
