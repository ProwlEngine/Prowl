// glslang rejects this shader (array init size); our compiler must reject it too.
#version 460
void main(){ int a[3] = int[2](1,2); }
