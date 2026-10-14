// glslang rejects this shader (redeclare global); our compiler must reject it too.
#version 460
float x; float x;
void main(){}
