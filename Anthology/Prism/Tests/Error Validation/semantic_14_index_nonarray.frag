// glslang rejects this shader (index nonarray); our compiler must reject it too.
#version 460
void main(){ int x; x[0] = 1; }
