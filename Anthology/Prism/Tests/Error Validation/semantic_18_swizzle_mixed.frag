// glslang rejects this shader (swizzle mixed); our compiler must reject it too.
#version 460
void main(){ vec4 v; float f = v.xr; }
