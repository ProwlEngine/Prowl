// glslang rejects this shader (do int); our compiler must reject it too.
#version 460
void main(){ do {} while(1); }
