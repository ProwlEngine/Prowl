// glslang rejects this shader (out non lvalue); our compiler must reject it too.
#version 460
void f(out float x){} void main(){ f(1.0); }
