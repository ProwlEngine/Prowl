#version 450
layout(location=0) out vec4 c;
void main(){ const int w = -8 >> 1u; const int x = -8 << 1u; c = vec4(float(w), float(x), 0, 0); }
