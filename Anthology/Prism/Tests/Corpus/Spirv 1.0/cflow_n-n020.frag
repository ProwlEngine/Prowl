#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
precise float f(float a, float b){ return a*b + a; } void main(){ o = vec4(f(vin.x, vin.y)); }
