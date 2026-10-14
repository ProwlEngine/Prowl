// glslang rejects this shader (float to int); our compiler must reject it too.
#version 460
void main(){ int x = 1.0; }
