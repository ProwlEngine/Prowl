#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii;
layout(location=0) out vec4 o;
float f(const float a); void main(){ o=vec4(f(vin.x)); } float f(float b){ return b*2.0; }
