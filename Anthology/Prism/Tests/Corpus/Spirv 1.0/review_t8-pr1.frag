#version 450
layout(location=0) in vec4 v;
layout(location=0) out vec4 c;
precise float f(float a, float b){ return a*b+a; }
void main(){ c = vec4(f(v.x, v.y)); }
