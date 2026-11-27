// glslang rejects this shader (defined no arg); our compiler must reject it too.
#version 460
#if defined
void main(){}
#endif
