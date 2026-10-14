// glslang rejects this shader (void var); our compiler must reject it too.
#version 460
void main(){ void v; }
