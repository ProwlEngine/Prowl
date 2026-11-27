#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
float f(){ return 1.0; } void main(){ float f = 2.0; o=vec4(f()); }
