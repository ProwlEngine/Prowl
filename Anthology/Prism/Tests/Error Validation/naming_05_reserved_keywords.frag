// glslang rejects this shader (reserved keywords); our compiler must reject it too.
#version 460
void main(){ int in_out; float common; float partition; }
