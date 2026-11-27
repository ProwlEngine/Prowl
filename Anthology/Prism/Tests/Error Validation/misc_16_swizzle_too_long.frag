// glslang rejects this shader (swizzle too long); our compiler must reject it too.
#version 460
void main(){ vec3 v; v.xyzw; }
