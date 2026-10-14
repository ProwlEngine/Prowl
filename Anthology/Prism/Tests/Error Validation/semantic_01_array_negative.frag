// glslang rejects this shader (array negative); our compiler must reject it too.
#version 460
void main(){ float a[-1]; }
