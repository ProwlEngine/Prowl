#version 450
layout(constant_id=3) const float D = 2.0;
layout(constant_id=4) const double E = 1.0;
layout(constant_id=5) const int I = 1;
const float F = D * 2.0;
layout(location=0) out vec4 o;
void main(){ o = vec4(D, float(E), F, float(I)); }
