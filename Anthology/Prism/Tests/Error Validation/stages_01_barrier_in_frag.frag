// glslang rejects this shader (barrier in frag); our compiler must reject it too.
#version 460
void main(){ barrier(); }
