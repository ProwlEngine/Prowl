// glslang rejects this shader (swizzle chain oob); our compiler must reject it too.
#version 460
void main(){ vec4 v; v.xyzw.xy.xyz; }
