// glslang rejects this shader (if unbalanced); our compiler must reject it too.
#version 460
#if (1
void main(){}
#endif
