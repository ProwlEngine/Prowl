// glslang rejects this shader (sin too many); our compiler must reject it too.
#version 460
void main(){ float f = sin(1.0, 2.0); }
