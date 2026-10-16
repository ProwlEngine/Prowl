#version 450
layout(constant_id=2147483647) const int A = 1;
layout(location=0) out vec4 o;
void main(){ o = vec4(A); }
