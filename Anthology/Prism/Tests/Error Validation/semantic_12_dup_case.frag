// glslang rejects this shader (dup case); our compiler must reject it too.
#version 460
void main(){ switch(1){ case 1: case 1: break; } }
