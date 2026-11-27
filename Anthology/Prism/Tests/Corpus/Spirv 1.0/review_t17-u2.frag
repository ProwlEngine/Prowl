#version 450
layout(location=0) out vec4 c;
void main(){ const uint a = uint(-1.0); const int b = int(3e9); const uint d = uint(5e9); c = vec4(float(a), float(b), float(d), 1); }
