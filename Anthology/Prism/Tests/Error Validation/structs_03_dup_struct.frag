// glslang rejects this shader (dup struct); our compiler must reject it too.
#version 460
struct S { float a; };
struct S { float b; };
void main(){}
