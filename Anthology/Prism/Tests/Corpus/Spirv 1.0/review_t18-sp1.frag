#version 450
layout(constant_id=0) const int a = 2 + 3;
layout(location=0) out vec4 c;
void main(){ c = vec4(float(a)); }
