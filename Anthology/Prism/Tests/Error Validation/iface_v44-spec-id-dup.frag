#version 450
layout(constant_id=1) const int A = 1;
layout(constant_id=1) const int B = 2;
layout(location=0) out vec4 o;
void main(){ o = vec4(A+B); }
