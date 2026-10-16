// glslang rejects this shader (array size assign); our compiler must reject it too.
#version 460
void main(){ int a[3]; int b[4]; a = b; }
