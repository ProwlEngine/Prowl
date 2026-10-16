#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
float x=1.0; void f(float x){ { float x = 2.0; o.x=x; } o.y=x; } void main(){ o=vec4(0); f(3.0); float x=x+1.0; o.z=x; }
