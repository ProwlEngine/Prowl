#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
float f(float x){ return 1.0; } float f(double x){ return 2.0; } void main(){ o = vec4(f(i)); }
