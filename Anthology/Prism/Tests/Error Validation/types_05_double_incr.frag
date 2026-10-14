// glslang rejects this shader (double incr); our compiler must reject it too.
#version 460
void main(){ int i; i = i++ ++; }
