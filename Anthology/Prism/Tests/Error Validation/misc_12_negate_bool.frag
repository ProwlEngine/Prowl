// glslang rejects this shader (negate bool); our compiler must reject it too.
#version 460
void main(){ float f = -true; }
