// glslang rejects this shader (ident starts digit); our compiler must reject it too.
#version 460
void main(){ float 1x = 0.0; }
