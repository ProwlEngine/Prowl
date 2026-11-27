#version 450
layout(constant_id=0) const uint a = 3u;
const int b = int(a) + 1;
const bool t = a > 2u;
layout(location=0) out vec4 c;
void main(){ c = vec4(float(b), t, 0, 0); }
