#version 450
layout(location=0) out vec4 c;
void main(){ const bool a = -1u > 0u; const bool b = ~0u > 5u; const bool d = (-2u) / 2u == 2147483647u; c = vec4(a, b, d, 1); }
