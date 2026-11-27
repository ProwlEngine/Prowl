#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
float f(out float x){ x=1.0; return 2.0; } void main(){ int i; o=vec4(f(i)); }
