// glslang rejects this shader (dot one arg); our compiler must reject it too.
#version 460
void main(){ vec2 v; float f = dot(v); }
