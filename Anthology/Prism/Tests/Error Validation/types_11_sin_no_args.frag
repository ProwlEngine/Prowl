// glslang rejects this shader (sin no args); our compiler must reject it too.
#version 460
void main(){ float f = sin(); }
