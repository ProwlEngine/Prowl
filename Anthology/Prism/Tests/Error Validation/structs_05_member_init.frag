// glslang rejects this shader (member init); our compiler must reject it too.
#version 460
struct S { float a = 1.0; };
void main(){}
