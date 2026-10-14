// glslang rejects this shader (length on scalar); our compiler must reject it too.
#version 460
void main(){ float a=1.0; float b = a.length(); }
