// glslang rejects this shader (clamp one arg); our compiler must reject it too.
#version 460
void main(){ float f = clamp(1.0); }
