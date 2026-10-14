// glslang rejects this shader (swizzle dup lvalue); our compiler must reject it too.
#version 460
void main(){ vec2 v; v.xx = vec2(1.0); }
