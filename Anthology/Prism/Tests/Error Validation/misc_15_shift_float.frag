// glslang rejects this shader (shift float); our compiler must reject it too.
#version 460
void main(){ int a = 1 << 2.0; }
