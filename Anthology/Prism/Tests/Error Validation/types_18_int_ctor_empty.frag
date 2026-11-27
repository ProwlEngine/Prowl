// glslang rejects this shader (int ctor empty); our compiler must reject it too.
#version 460
void main(){ int i = int(); }
