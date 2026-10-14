// glslang rejects this shader (array zero); our compiler must reject it too.
#version 460
void main(){ float a[0]; }
