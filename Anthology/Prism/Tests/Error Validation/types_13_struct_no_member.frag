// glslang rejects this shader (struct no member); our compiler must reject it too.
#version 460
void main(){ struct S{int a;}; S s; float f=s.b; }
