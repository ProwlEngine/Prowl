// glslang rejects this shader (dup definition); our compiler must reject it too.
#version 460
void f(float a){} void f(float a){}
void main(){ f(1.0); }
