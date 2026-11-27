// glslang rejects this shader (uint to int); our compiler must reject it too.
#version 460
void main(){ uint u = -1; int i = u; }
