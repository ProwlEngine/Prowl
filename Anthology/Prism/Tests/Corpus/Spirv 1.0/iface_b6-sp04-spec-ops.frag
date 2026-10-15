#version 450
layout(constant_id=0) const int A = 3;
layout(constant_id=1) const uint B = 4u;
layout(constant_id=2) const bool C = false;
const int X = A * 2 + int(B) - (A << 1) % 3;
const bool Y = C || (A > 2) && !C;
const uint Z = uint(A) + B ^ 7u;
const ivec2 V = ivec2(A, X);
const int S = V.y + (C ? 1 : 2);
layout(location=0) out vec4 o;
void main(){ o = vec4(X, Y ? 1 : 0, Z, S) + vec4(V.y); }
