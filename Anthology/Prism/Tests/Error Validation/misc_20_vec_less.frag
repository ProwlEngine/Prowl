// glslang rejects this shader (vec less); our compiler must reject it too.
#version 460
void main(){ vec2 a; vec2 b; bool c = a < b; }
