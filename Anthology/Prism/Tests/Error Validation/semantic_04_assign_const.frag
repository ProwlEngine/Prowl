// glslang rejects this shader (assign const); our compiler must reject it too.
#version 460
void main(){ const float c = 1.0; c = 2.0; }
