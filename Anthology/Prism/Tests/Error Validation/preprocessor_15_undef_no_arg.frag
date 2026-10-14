// glslang rejects this shader (undef no arg); our compiler must reject it too.
#version 460
#undef
void main(){}
