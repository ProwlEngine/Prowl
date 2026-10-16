// glslang rejects this shader (elif without if); our compiler must reject it too.
#version 460
#elif 1
void main(){}
