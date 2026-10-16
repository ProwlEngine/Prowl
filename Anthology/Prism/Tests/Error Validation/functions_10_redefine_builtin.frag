// glslang rejects this shader (redefine builtin); our compiler must reject it too.
#version 460
float sin(float x){ return x; }
void main(){}
