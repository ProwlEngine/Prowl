// glslang rejects this shader (if trailing op); our compiler must reject it too.
#version 460
#if 1 +
void main(){}
#endif
