// glslang rejects this shader (assign row scalar); our compiler must reject it too.
#version 460
void main(){ int a[2][3]; a[0] = 1; }
