#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
layout(std430,binding=0) buffer B { int d[]; };
void f(){} void main(){ d[0] > 0 ? f() : f(); o = vin; }
