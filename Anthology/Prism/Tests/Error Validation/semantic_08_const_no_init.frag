// glslang rejects this shader (const no init); our compiler must reject it too.
#version 460
void main(){ const float c; }
