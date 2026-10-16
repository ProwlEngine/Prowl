// glslang rejects this shader (macro arg count); our compiler must reject it too.
#version 460
#define M(a,b) a
void main(){ float f = M(1.0); }
