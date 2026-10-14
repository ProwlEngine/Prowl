// glslang rejects this shader (break outside loop); our compiler must reject it too.
#version 460
void main(){ break; }
