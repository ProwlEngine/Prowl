#version 450
layout(constant_id=1) const float SCALE = 1.0;
layout(location=0) out vec4 o;
void main(){ o = vec4(SCALE); }