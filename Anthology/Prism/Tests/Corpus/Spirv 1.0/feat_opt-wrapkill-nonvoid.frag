#version 450
#extension GL_EXT_terminate_invocation : require
layout(location=0) out vec4 o;
layout(location=0) flat in int n;
layout(location=1) in float f;
float stop(float x){ if (x < 0.0) discard; return x * 2.0; }
void quit(){ terminateInvocation; }
void main(){ float acc = 0.0; for (int i = 0; i < n; i++, acc += stop(f)) { if (f > 4.0) quit(); acc += 1.0; } o = vec4(acc); }
