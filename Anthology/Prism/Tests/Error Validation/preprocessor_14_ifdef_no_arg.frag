// glslang rejects this shader (ifdef no arg); our compiler must reject it too.
#version 460
#ifdef
void main(){}
#endif
