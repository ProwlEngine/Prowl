#version 450
layout(location=0) in vec4 inA;
layout(location=1) in vec2 inB;
layout(location=2) in flat int inC;
layout(location=0) out vec4 outA;
layout(location=1) out vec4 outB;
vec4 readA() { return inA; }
vec2 readB() { return inB; }
void writeB(vec4 v) { outB = v; }
void main() { outA = readA() + vec4(readB(), float(inC), 0.0); writeB(outA * 2.0); }
