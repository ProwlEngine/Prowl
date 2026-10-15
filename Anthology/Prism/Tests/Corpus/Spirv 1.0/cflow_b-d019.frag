#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
float f(vec2 x){ return 1.0; } float f(dvec2 x){ return 2.0; } void main(){ o=vec4(f(ivec2(ii))); }
