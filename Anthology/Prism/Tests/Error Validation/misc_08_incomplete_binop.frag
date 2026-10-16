// glslang rejects this shader (incomplete binop); our compiler must reject it too.
#version 460
void main(){ int x = (1 + ); }
