// glslang rejects this shader (empty struct); our compiler must reject it too.
#version 460
struct S {};
void main(){}
