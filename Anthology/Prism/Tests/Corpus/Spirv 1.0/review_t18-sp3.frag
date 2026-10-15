#version 450
layout(constant_id=0) const int a = 3;
const int b = a << 2u;
const uint d = 5u >> a;
layout(location=0) out vec4 c;
void main(){ c = vec4(float(b), float(d), 0, 0); }
