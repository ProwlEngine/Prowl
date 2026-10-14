// glslang rejects this shader (array nonconst); our compiler must reject it too.
#version 460
void main(){ int n=3; float a[n]; }
