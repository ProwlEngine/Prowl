#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
void f(out int x){ x=3; } void main(){ float y=1.0; f(y); o=vec4(y); }
