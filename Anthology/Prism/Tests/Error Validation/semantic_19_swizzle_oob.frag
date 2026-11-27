// glslang rejects this shader (swizzle oob); our compiler must reject it too.
#version 460
void main(){ vec2 v; float f = v.z; }
