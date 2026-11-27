// glslang rejects this shader (bool to int); our compiler must reject it too.
#version 460
void main(){ int i = true; }
