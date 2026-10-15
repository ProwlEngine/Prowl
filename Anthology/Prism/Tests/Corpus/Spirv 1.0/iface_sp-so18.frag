#version 450
layout(constant_id=0) const int A = 3;
layout(constant_id=1) const uint B = 4u;
layout(constant_id=2) const bool C = false;
layout(location=0) out vec4 o;
const ivec2 V = ivec2(A,B).yx;
void main(){ o = vec4(float(V.x)); }
